using BCrypt.Net;
using AdminDashboard.Data;
using AdminDashboard.Entities;
using AdminDashboard.Hubs;
using AdminDashboard.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AdminDashboard.Controllers;

[ApiController]
[Route("api/driver")]
public class DriverController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IOptions<AppSecurityOptions> _opts;
    private readonly IHubContext<TrackingHub> _trackingHub;
    private readonly IHubContext<NotifyHub> _notifyHub;
    private readonly NotificationService _notifications;

    private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0;
        double dLat = (lat2 - lat1) * Math.PI / 180.0;
        double dLon = (lon2 - lon1) * Math.PI / 180.0;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double NormalizeSpeedMps(LocationReq req)
    {
        const double maxKmh = 160.0; 
        var maxMps = maxKmh / 3.6;

        if (req.SpeedKmh.HasValue)
        {
            var kmh = Math.Clamp(req.SpeedKmh.Value, 0.0, maxKmh);
            return Math.Clamp(kmh / 3.6, 0.0, maxMps);
        }

        var mps = req.SpeedMps;
        if (double.IsNaN(mps) || double.IsInfinity(mps) || mps < 0) mps = 0;

        if (mps > 80.0)
        {
            var kmh = Math.Clamp(mps, 0.0, maxKmh);
            return Math.Clamp(kmh / 3.6, 0.0, maxMps);
        }

        return Math.Clamp(mps, 0.0, maxMps);
    }

    public DriverController(AppDbContext db, IOptions<AppSecurityOptions> opts, IHubContext<TrackingHub> trackingHub, IHubContext<NotifyHub> notifyHub, NotificationService notifications)
    {
        _db = db;
        _opts = opts;
        _trackingHub = trackingHub;
        _notifyHub = notifyHub;
        _notifications = notifications;
    }

    public record LoginReq(string Phone, string Pin);

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginReq req)
    {
        
        var d = await _db.Drivers.FirstOrDefaultAsync(x => x.Phone == req.Phone);
        if (d != null)
        {
            bool pinValid;
            if (!string.IsNullOrWhiteSpace(d.PasswordHash))
            {
                pinValid = BCrypt.Net.BCrypt.Verify(req.Pin, d.PasswordHash);
            }
            else
            {
                pinValid = (d.Pin == req.Pin);
                if (pinValid)
                    d.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Pin, workFactor: 12);
            }
            if (!pinValid) d = null;
        }
        if (d == null) return Unauthorized(new { error = "بيانات الدخول غير صحيحة" });

        d.Status = DriverStatus.Available;
        await _db.SaveChangesAsync();

        var token = DriverAuth.IssueToken(d.Id, _opts);
        await _notifyHub.Clients.Group("admin").SendAsync("driver_status", new { driverId = d.Id, status = d.Status });

        await _notifications.CreateAndBroadcastAsync(NotificationUserType.Admin, null,
            "سائق متصل", $"السائق {d.Name} أصبح متصلاً", null);

        return Ok(new { token, driver = new { d.Id, d.Name, d.Phone, d.VehicleType, d.Status, d.PhotoUrl } });
    }

    private bool TryGetDriverId(out int driverId)
    {
        driverId = 0;
        if (!Request.Headers.TryGetValue("X-DRIVER-TOKEN", out var token)) return false;
        return DriverAuth.TryValidate(token!, _opts, out driverId);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        if (!TryGetDriverId(out var driverId)) return Unauthorized(new { error = "unauthorized" });
        var d = await _db.Drivers.FirstOrDefaultAsync(x => x.Id == driverId);
        if (d == null) return Unauthorized(new { error = "unauthorized" });
        d.Status = DriverStatus.Offline;
        await _db.SaveChangesAsync();
        await _notifyHub.Clients.Group("admin").SendAsync("driver_status", new { driverId = d.Id, status = d.Status });
        await _notifications.CreateAndBroadcastAsync(NotificationUserType.Admin, null,
            "سائق غير متصل", $"السائق {d.Name} قام بتسجيل الخروج", null);
        return Ok(new { ok = true });
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        if (!TryGetDriverId(out var driverId)) return Unauthorized(new { error = "unauthorized" });
        var d = await _db.Drivers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == driverId);
        if (d == null) return Unauthorized(new { error = "unauthorized" });
        return Ok(new { d.Id, d.Name, d.Phone, d.VehicleType, d.Status, d.PhotoUrl });
    }

    [HttpGet("current-order")]
    public async Task<IActionResult> CurrentOrder()
    {
        if (!TryGetDriverId(out var driverId)) return Unauthorized(new { error = "unauthorized" });

        var o = await _db.Orders.AsNoTracking()
            .Where(x => x.DriverId == driverId && x.CurrentStatus != OrderStatus.Delivered && x.CurrentStatus != OrderStatus.Cancelled)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync();

        if (o == null) return Ok(new { hasOrder = false });

        var s = await _db.StoreSettings.AsNoTracking().FirstOrDefaultAsync();
        var storeLat = s?.StoreLat ?? 0.0;
        var storeLng = s?.StoreLng ?? 0.0;

        return Ok(new
        {
            hasOrder = true,
            o.Id,
            o.CurrentStatus,
            o.DeliveryLat,
            o.DeliveryLng,
            o.DeliveryAddress,
            storeLat,
            storeLng,
            o.Notes,
            o.Total
        });
    }

    [HttpGet("active-orders")]
    public async Task<IActionResult> ActiveOrders()
    {
        if (!TryGetDriverId(out var driverId)) return Unauthorized(new { error = "unauthorized" });

        var s = await _db.StoreSettings.AsNoTracking().FirstOrDefaultAsync();
        var storeLat = s?.StoreLat ?? 0.0;
        var storeLng = s?.StoreLng ?? 0.0;

        var driver = await _db.Drivers.AsNoTracking().FirstOrDefaultAsync(d => d.Id == driverId);
        var vehicleType = driver?.VehicleType ?? VehicleType.Car;
        var bikeSpeed = (double?)(s?.DriverSpeedBikeKmH) ?? 18.0;
        var carSpeed = (double?)(s?.DriverSpeedCarKmH) ?? 30.0;
        var speedKmH = vehicleType == VehicleType.Bike ? bikeSpeed : carSpeed;
        if (speedKmH <= 0) speedKmH = 30.0;

        var ordersRaw = await _db.Orders.AsNoTracking()
            .Include(o => o.Customer)
            .Where(o => o.DriverId == driverId && o.CurrentStatus != OrderStatus.Delivered && o.CurrentStatus != OrderStatus.Cancelled)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Take(10)
            .Select(o => new
            {
                o.Id,
                o.CurrentStatus,
                o.DeliveryLat,
                o.DeliveryLng,
                o.DeliveryAddress,
                o.Notes,
                o.Total,
                customerName = o.Customer != null ? o.Customer.Name : "",
                customerPhone = o.Customer != null ? o.Customer.Phone : ""
            })
            .ToListAsync();

        var orders = ordersRaw.Select(o =>
        {
            var lat = o.DeliveryLat;
            var lng = o.DeliveryLng;
            int? etaMinutes = null;
            if (storeLat != 0 && storeLng != 0 && lat != 0 && lng != 0)
            {
                var km = HaversineKm(storeLat, storeLng, lat, lng);
                etaMinutes = (int)Math.Max(1, Math.Round((km / speedKmH) * 60.0));
            }
            return new
            {
                o.Id,
                o.CurrentStatus,
                o.DeliveryLat,
                o.DeliveryLng,
                o.DeliveryAddress,
                o.Notes,
                o.Total,
                o.customerName,
                o.customerPhone,
                etaMinutes
            };
        }).ToList();

        return Ok(new { storeLat, storeLng, vehicleType, speedKmH, orders });
    }

    public record UpdateOrderStatusReq(int OrderId, OrderStatus Status, string? Comment);

    [HttpPost("order-status")]
    public async Task<IActionResult> UpdateOrderStatus(UpdateOrderStatusReq req)
    {
        if (!TryGetDriverId(out var driverId)) return Unauthorized(new { error = "unauthorized" });
        var o = await _db.Orders.FirstOrDefaultAsync(x => x.Id == req.OrderId && x.DriverId == driverId);
        if (o == null) return NotFound(new { error = "not_found" });

        o.CurrentStatus = req.Status;
        _db.OrderStatusHistory.Add(new OrderStatusHistory { OrderId = o.Id, Status = req.Status, Comment = req.Comment, ChangedByType = "driver", ChangedById = driverId });

        if (req.Status == OrderStatus.WithDriver && o.DriverConfirmedAtUtc == null)
        {
            o.DriverConfirmedAtUtc = DateTime.UtcNow;
        }

        if (req.Status == OrderStatus.WithDriver)
        {
            var d = await _db.Drivers.FindAsync(driverId);
            if (d != null) d.Status = DriverStatus.Busy;
        }
        
        if (req.Status == OrderStatus.Delivered)
        {
            o.DeliveredAtUtc = DateTime.UtcNow;

            var d = await _db.Drivers.FindAsync(driverId);
            if (d != null) d.Status = DriverStatus.Available;

            var existingCommission = await _db.AgentCommissions.AnyAsync(c => c.OrderId == o.Id);
            if (!existingCommission)
            {
                // نشمل جميع المندوبين المرتبطين بالطلب (مقبول أو تلقائي أو معلق)
                var agentItems = await _db.OrderAgentItems
                    .Where(ai => ai.OrderId == o.Id &&
                                 ai.AgentStatus != AgentOrderStatus.Rejected)
                    .ToListAsync();

                foreach (var agentItem in agentItems)
                {
                    var agent = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentItem.AgentId);
                    var commissionPercent = agentItem.CommissionPercent > 0
                        ? agentItem.CommissionPercent
                        : (agent?.CommissionPercent ?? 0m);
                    // إذا AgentSubtotal=0 نستخدم إجمالي الطلب كبديل
                    var saleAmount = agentItem.AgentSubtotal > 0 ? agentItem.AgentSubtotal : o.Total;
                    var commissionAmount = Math.Round(saleAmount * commissionPercent / 100m, 2);

                    _db.AgentCommissions.Add(new AgentCommission
                    {
                        AgentId = agentItem.AgentId,
                        OrderId = o.Id,
                        SaleAmount = saleAmount,
                        CommissionPercent = commissionPercent,
                        CommissionAmount = commissionAmount,
                        CreatedAtUtc = DateTime.UtcNow
                    });
                }
            }
        }

        await _db.SaveChangesAsync();

        await _notifyHub.Clients.Group("admin").SendAsync("order_status", new { orderId = o.Id, status = o.CurrentStatus, driverId = o.DriverId });
        await _notifyHub.Clients.Group($"customer-{o.CustomerId}").SendAsync("order_status", new { orderId = o.Id, status = o.CurrentStatus, driverId = o.DriverId });

var statusArabic = o.CurrentStatus switch
{
    OrderStatus.New => "جديد",
    OrderStatus.Confirmed => "مؤكد",
    OrderStatus.Preparing => "قيد المعالجة",
    OrderStatus.ReadyForPickup => "جاهز للاستلام",
    OrderStatus.WithDriver => "مع السائق",
    OrderStatus.Delivered => "تم التسليم",
    OrderStatus.Cancelled => "ملغي",
    OrderStatus.Accepted => "مقبول",
    _ => o.CurrentStatus.ToString()
};

await _notifications.CreateAndBroadcastAsync(NotificationUserType.Admin, null,
    "تحديث حالة", $"تم تحديث حالة الطلب #{o.Id} إلى {statusArabic}", o.Id);

        await _notifications.SendCustomerOrderStatusPushIfNeededAsync(o.CustomerId, o.Id, o.CurrentStatus, o.ProcessingEtaMinutes, o.DeliveryEtaMinutes);
        return Ok(new { ok = true });
    }

    public record LocationReq(double Lat, double Lng, double SpeedMps, double HeadingDeg, double AccuracyMeters, double? SpeedKmh = null);

    public record LocationBatchReq(List<LocationReq> Points);

    public record CancelOrderReq(string? Reason);

    [HttpPost("order/{orderId:int}/cancel")]
    public async Task<IActionResult> CancelOrder(int orderId, CancelOrderReq req)
    {
        if (!TryGetDriverId(out var driverId)) return Unauthorized(new { error = "unauthorized" });

        var o = await _db.Orders.FirstOrDefaultAsync(x => x.Id == orderId && x.DriverId == driverId);
        if (o == null) return NotFound(new { error = "not_found" });

        if (o.CurrentStatus == OrderStatus.Delivered || o.CurrentStatus == OrderStatus.Cancelled)
            return BadRequest(new { error = "cannot_cancel", message = "لا يمكن إلغاء هذا الطلب" });

        o.CurrentStatus = OrderStatus.Cancelled;
        o.CancelReasonCode = "driver_cancel";

        var comment = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim();
        _db.OrderStatusHistory.Add(new OrderStatusHistory
        {
            OrderId = o.Id,
            Status = OrderStatus.Cancelled,
            ReasonCode = "driver_cancel",
            Comment = "ملغي من قبل السائق" + (comment == null ? "" : $" — {comment}"),
            ChangedByType = "driver",
            ChangedById = driverId,
            ChangedAtUtc = DateTime.UtcNow
        });

        var d = await _db.Drivers.FindAsync(driverId);
        if (d != null) d.Status = DriverStatus.Available;

        await _db.SaveChangesAsync();

        await _notifyHub.Clients.Group("admin").SendAsync("order_status", new { orderId = o.Id, status = o.CurrentStatus, driverId = o.DriverId });
        await _notifyHub.Clients.Group($"customer-{o.CustomerId}").SendAsync("order_status", new { orderId = o.Id, status = o.CurrentStatus, driverId = o.DriverId });

        await _notifications.CreateAndBroadcastAsync(NotificationUserType.Admin, null,
            "إلغاء من السائق", $"السائق ألغى الطلب #{o.Id}" + (comment == null ? "" : $" — {comment}"), o.Id);

        await _notifications.CreateAndBroadcastAsync(NotificationUserType.Customer, o.CustomerId,
            "تحديث الطلب", $"تم إلغاء الطلب #{o.Id} من قبل السائق.", o.Id);

        return Ok(new { ok = true });
    }

    [HttpPost("location")]
    public async Task<IActionResult> UpsertLocation(LocationReq req)
    {
        if (!TryGetDriverId(out var driverId)) return Unauthorized(new { error = "unauthorized" });

        var (activeOrder, loc) = await UpsertLocationCore(driverId, req);

        var adminOrders = await _db.Orders.AsNoTracking()
            .Where(o => o.DriverId == driverId && o.CurrentStatus != OrderStatus.Delivered && o.CurrentStatus != OrderStatus.Cancelled)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Select(o => new { o.Id, o.CurrentStatus })
            .Take(10)
            .ToListAsync();
        if (adminOrders.Count > 0)
        {
            var adminPayload = new
            {
                driverId,
                lat = loc.Lat,
                lng = loc.Lng,
                speedMps = loc.SpeedMps,
                headingDeg = loc.HeadingDeg,
                accuracyMeters = loc.AccuracyMeters,
                updatedAtUtc = loc.UpdatedAtUtc,
                activeOrders = adminOrders
            };
            await _trackingHub.Clients.Group("admin").SendAsync("driver_location", adminPayload);
            await _trackingHub.Clients.Group("admin").SendAsync("driver_location_updated", adminPayload);
        }

        if (activeOrder != null && activeOrder.CurrentStatus == OrderStatus.WithDriver)
        {
            var payload = new
            {
                orderId = activeOrder.Id,
                driverId,
                lat = loc.Lat,
                lng = loc.Lng,
                speedMps = loc.SpeedMps,
                headingDeg = loc.HeadingDeg,
                accuracyMeters = loc.AccuracyMeters,
                updatedAtUtc = loc.UpdatedAtUtc
            };

            await _trackingHub.Clients.Group($"customer-{activeOrder.CustomerId}").SendAsync("driver_location", payload);
            await _trackingHub.Clients.Group($"customer-{activeOrder.CustomerId}").SendAsync("driver_location_updated", payload);
        }

        return Ok(new { ok = true });
    }

    [HttpPost("location/batch")]
    public async Task<IActionResult> UpsertLocationBatch(LocationBatchReq req)
    {
        if (!TryGetDriverId(out var driverId)) return Unauthorized(new { error = "unauthorized" });
        if (req.Points == null || req.Points.Count == 0) return Ok(new { ok = true });

        (dynamic? activeOrder, DriverLocation? lastLoc) result = (null, null);
        foreach (var p in req.Points)
        {
            result = await UpsertLocationCore(driverId, p);
        }

        var activeOrder = result.activeOrder;
        var loc = result.lastLoc;
        if (loc == null) return Ok(new { ok = true });

        var adminOrders = await _db.Orders.AsNoTracking()
            .Where(o => o.DriverId == driverId && o.CurrentStatus != OrderStatus.Delivered && o.CurrentStatus != OrderStatus.Cancelled)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Select(o => new { o.Id, o.CurrentStatus })
            .Take(10)
            .ToListAsync();
        if (adminOrders.Count > 0)
        {
            var adminPayload = new
            {
                driverId,
                lat = loc.Lat,
                lng = loc.Lng,
                speedMps = loc.SpeedMps,
                headingDeg = loc.HeadingDeg,
                accuracyMeters = loc.AccuracyMeters,
                updatedAtUtc = loc.UpdatedAtUtc,
                activeOrders = adminOrders
            };
            await _trackingHub.Clients.Group("admin").SendAsync("driver_location", adminPayload);
            await _trackingHub.Clients.Group("admin").SendAsync("driver_location_updated", adminPayload);
        }

        if (activeOrder != null && activeOrder.CurrentStatus == OrderStatus.WithDriver)
        {
            var payload = new
            {
                orderId = activeOrder.Id,
                driverId,
                lat = loc.Lat,
                lng = loc.Lng,
                speedMps = loc.SpeedMps,
                headingDeg = loc.HeadingDeg,
                accuracyMeters = loc.AccuracyMeters,
                updatedAtUtc = loc.UpdatedAtUtc
            };
            await _trackingHub.Clients.Group($"customer-{activeOrder.CustomerId}").SendAsync("driver_location", payload);
            await _trackingHub.Clients.Group($"customer-{activeOrder.CustomerId}").SendAsync("driver_location_updated", payload);
        }

        return Ok(new { ok = true });
    }

    private async Task<(dynamic? activeOrder, DriverLocation loc)> UpsertLocationCore(int driverId, LocationReq req)
    {
        
        var activeOrder = await _db.Orders.AsNoTracking()
            .Where(o => o.DriverId == driverId && o.CurrentStatus != OrderStatus.Delivered && o.CurrentStatus != OrderStatus.Cancelled)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Select(o => new { o.Id, o.CustomerId, o.CurrentStatus, o.DriverConfirmedAtUtc })
            .FirstOrDefaultAsync();

        var loc = await _db.DriverLocations.FirstOrDefaultAsync(x => x.DriverId == driverId);
        if (loc == null)
        {
            loc = new DriverLocation { DriverId = driverId };
            _db.DriverLocations.Add(loc);
        }

        var normalizedSpeedMps = NormalizeSpeedMps(req);

        if (loc.SpeedMps > 0 && normalizedSpeedMps > 0)
        {
            normalizedSpeedMps = (loc.SpeedMps * 0.7) + (normalizedSpeedMps * 0.3);
        }

        loc.Lat = req.Lat;
        loc.Lng = req.Lng;
        loc.SpeedMps = normalizedSpeedMps;
        loc.HeadingDeg = (double.IsNaN(req.HeadingDeg) || double.IsInfinity(req.HeadingDeg)) ? 0 : req.HeadingDeg;
        loc.AccuracyMeters = (double.IsNaN(req.AccuracyMeters) || double.IsInfinity(req.AccuracyMeters)) ? 0 : req.AccuracyMeters;
        loc.UpdatedAtUtc = DateTime.UtcNow;

        if (activeOrder != null && activeOrder.CurrentStatus == OrderStatus.WithDriver)
        {
            var now = DateTime.UtcNow;

            var lastPt = await _db.DriverTrackPoints.AsNoTracking()
                .Where(p => p.OrderId == activeOrder.Id)
                .OrderByDescending(p => p.CreatedAtUtc)
                .Select(p => new { p.Lat, p.Lng, p.CreatedAtUtc })
                .FirstOrDefaultAsync();

            var incKm = 0.0;
            if (lastPt != null)
            {
                incKm = HaversineKm(lastPt.Lat, lastPt.Lng, req.Lat, req.Lng);
                
                if (incKm > 1.0) incKm = 0.0;
            }

            _db.DriverTrackPoints.Add(new DriverTrackPoint
            {
                DriverId = driverId,
                OrderId = activeOrder.Id,
                Lat = req.Lat,
                Lng = req.Lng,
                SpeedMps = normalizedSpeedMps,
                HeadingDeg = req.HeadingDeg,
                CreatedAtUtc = now
            });

            if (incKm > 0)
            {
                var ord = await _db.Orders.FirstOrDefaultAsync(x => x.Id == activeOrder.Id);
                if (ord != null)
                {
                    ord.DistanceKm = Math.Round(Math.Max(0, ord.DistanceKm) + incKm, 3);
                }
            }
        }

        await _db.SaveChangesAsync();

        var keep = 500;
        var cnt = await _db.DriverTrackPoints.CountAsync(x => x.DriverId == driverId);
        if (cnt > keep)
        {
            var ids = await _db.DriverTrackPoints.AsNoTracking()
                .Where(x => x.DriverId == driverId)
                .OrderByDescending(x => x.CreatedAtUtc)
                .Skip(keep)
                .Select(x => x.Id)
                .ToListAsync();
            if (ids.Count > 0)
            {
                var del = _db.DriverTrackPoints.Where(x => ids.Contains(x.Id));
                _db.DriverTrackPoints.RemoveRange(del);
                await _db.SaveChangesAsync();
            }
        }

        return (activeOrder, loc);
    }

    [HttpGet("today-stats")]
    public async Task<IActionResult> GetTodayStats()
    {
        if (!TryGetDriverId(out var driverId)) return Unauthorized(new { error = "unauthorized" });

        var driver = await _db.Drivers.AsNoTracking().FirstOrDefaultAsync(d => d.Id == driverId);
        if (driver == null) return NotFound(new { error = "driver_not_found" });

        var localNow = DateTime.Now;
        var localStart = new DateTime(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, DateTimeKind.Local);
        var localEnd = localStart.AddDays(1);

        var startUtc = localStart.ToUniversalTime();
        var endUtc = localEnd.ToUniversalTime();

        // الطلبات المُسلَّمة اليوم - نستخدم DeliveredAtUtc إذا موجود وإلا CreatedAtUtc
        var deliveredOrders = await _db.Orders.AsNoTracking()
            .Where(o => o.DriverId == driverId && o.CurrentStatus == OrderStatus.Delivered
                && (o.DeliveredAtUtc.HasValue
                    ? (o.DeliveredAtUtc >= startUtc && o.DeliveredAtUtc < endUtc)
                    : (o.CreatedAtUtc >= startUtc && o.CreatedAtUtc < endUtc)))
            .Select(o => new { o.Total, o.DeliveryFee })
            .ToListAsync();

        // الطلبات قيد التسليم الآن
        var inProgressCount = await _db.Orders.AsNoTracking()
            .CountAsync(o => o.DriverId == driverId
                && (o.CurrentStatus == OrderStatus.WithDriver
                    || o.CurrentStatus == OrderStatus.Accepted
                    || o.CurrentStatus == OrderStatus.ReadyForPickup
                    || o.CurrentStatus == OrderStatus.Confirmed
                    || o.CurrentStatus == OrderStatus.Preparing));

        var deliveredCount    = deliveredOrders.Count;
        var cashCollected     = deliveredOrders.Sum(o => o.Total);
        var totalDeliveryFees = deliveredOrders.Sum(o => o.DeliveryFee);
        var avgOrderValue     = deliveredCount > 0 ? Math.Round(cashCollected / deliveredCount, 2) : 0m;

        // العمولة تُحسب من رسوم التوصيل
        var commissionPercent = driver.CommissionPercent > 0 ? driver.CommissionPercent : 5m;
        var estimatedEarnings = Math.Round(totalDeliveryFees * commissionPercent / 100m, 2);

        return Ok(new 
        { 
            driverId,
            deliveredCount,
            inProgressCount,
            cashCollected,
            totalDeliveryFees,
            avgOrderValue,
            commissionPercent,
            estimatedEarnings,
            timestamp = DateTime.UtcNow
        });
    }

    [HttpGet("stats/monthly")]
    public async Task<IActionResult> GetMonthlyStats()
    {
        if (!TryGetDriverId(out var driverId)) return Unauthorized(new { error = "unauthorized" });

        var localNow = DateTime.Now;
        var monthStart = new DateTime(localNow.Year, localNow.Month, 1, 0, 0, 0, DateTimeKind.Local);
        var monthEnd = monthStart.AddMonths(1);

        var startUtc = monthStart.ToUniversalTime();
        var endUtc = monthEnd.ToUniversalTime();

        var driverMonthly = await _db.Drivers.AsNoTracking().Where(d => d.Id == driverId).Select(d => new { d.CommissionPercent }).FirstOrDefaultAsync();
        var commissionPercentMonthly = driverMonthly?.CommissionPercent > 0 ? driverMonthly.CommissionPercent : 5m;

        var monthlyOrders = await _db.Orders.AsNoTracking()
            .Where(o => o.DriverId == driverId && o.CurrentStatus == OrderStatus.Delivered
                && (o.DeliveredAtUtc.HasValue
                    ? (o.DeliveredAtUtc >= startUtc && o.DeliveredAtUtc < endUtc)
                    : (o.CreatedAtUtc >= startUtc && o.CreatedAtUtc < endUtc)))
            .Select(o => new { o.Total, o.DeliveryFee })
            .ToListAsync();

        var deliveredCount    = monthlyOrders.Count;
        var totalDeliveryFees = monthlyOrders.Sum(o => o.DeliveryFee);
        var totalOrdersAmount = monthlyOrders.Sum(o => o.Total);
        var commissionPercent = commissionPercentMonthly;
        var commissionEarnings = Math.Round(totalDeliveryFees * commissionPercent / 100m, 2);

        return Ok(new { driverId, deliveredCount, totalOrdersAmount, totalDeliveryFees, commissionPercent, commissionEarnings });
    }

}
