using BCrypt.Net;
using AdminDashboard.Data;
using AdminDashboard.Entities;
using AdminDashboard.Hubs;
using AdminDashboard.Security;
using AdminDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace AdminDashboard.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "AdminOnly")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHubContext<NotifyHub> _notifyHub;
    private readonly NotificationService _notifications;
    private readonly IOptions<AppSecurityOptions> _opts;

    public AdminController(AppDbContext db, IHubContext<NotifyHub> notifyHub, NotificationService notifications, IOptions<AppSecurityOptions> opts)
    {
        _db = db;
        _notifyHub = notifyHub;
        _notifications = notifications;
        _opts = opts;
    }

    [HttpGet("drivers")]
    public async Task<IActionResult> ListDrivers()
    {
        var list = await _db.Drivers.AsNoTracking().OrderBy(d => d.Id).ToListAsync();
        return Ok(list);
    }

    [HttpGet("drivers/{id:int}/track")]
    public async Task<IActionResult> GetDriverTrack(int id, [FromQuery] int limit = 300)
    {
        limit = Math.Clamp(limit, 10, 1000);
        var points = await _db.DriverTrackPoints.AsNoTracking()
            .Where(p => p.DriverId == id)
            .OrderByDescending(p => p.CreatedAtUtc)
            .Take(limit)
            .OrderBy(p => p.CreatedAtUtc)
            .Select(p => new { p.Lat, p.Lng, p.SpeedMps, p.HeadingDeg, p.CreatedAtUtc })
            .ToListAsync();

        return Ok(new { points });
    }

    public record UpsertDriverReq(int? Id, string Name, string Phone, string Pin, VehicleType VehicleType, DriverStatus Status, string? PhotoUrl);

    [HttpPost("drivers")]
    public async Task<IActionResult> UpsertDriver(UpsertDriverReq req)
    {
        Driver d;
        if (req.Id is null)
        {
            d = new Driver();
            _db.Drivers.Add(d);
        }
        else
        {
            d = await _db.Drivers.FirstOrDefaultAsync(x => x.Id == req.Id.Value) ?? new Driver();
            if (d.Id == 0) return NotFound(new { error = "not_found" });
        }

        d.Name = req.Name;
        d.Phone = req.Phone;
        d.VehicleType = req.VehicleType;
        d.Status = req.Status;
        d.PhotoUrl = req.PhotoUrl;

        if (!string.IsNullOrWhiteSpace(req.Pin))
        {
            d.Pin = req.Pin;
            d.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Pin, workFactor: 12);
        }

        await _db.SaveChangesAsync();
        await _notifyHub.Clients.Group("admin").SendAsync("driver_changed", new { d.Id });
        return Ok(new { d.Id, d.Name, d.Phone, d.Pin, d.VehicleType, d.Status, d.PhotoUrl, d.CommissionPercent, d.CreatedAtUtc });
    }

    [HttpDelete("drivers/{id:int}")]
    public async Task<IActionResult> DeleteDriver(int id)
    {
        var d = await _db.Drivers.FirstOrDefaultAsync(x => x.Id == id);
        if (d == null) return NotFound(new { error = "not_found" });

        var hasOrders = await _db.Orders.AsNoTracking().AnyAsync(o => o.DriverId == id);
        if (hasOrders)
        {
            return BadRequest(new
            {
                error = "has_orders",
                message = "لا يمكن حذف هذا السائق لأنه مرتبط بطلبات سابقة. يمكنك جعله غير متاح بدلاً من ذلك."
            });
        }

        _db.Drivers.Remove(d);
        try
        {
            await _db.SaveChangesAsync();
            await _notifyHub.Clients.Group("admin").SendAsync("driver_deleted", new { d.Id });
            return Ok(new { ok = true });
        }
        catch (DbUpdateException)
        {
            return BadRequest(new
            {
                error = "delete_failed",
                message = "تعذر حذف السائق لأنه مرتبط ببيانات أخرى."
            });
        }
    }

    [HttpGet("orders")]
    public async Task<IActionResult> ListOrders([FromQuery] bool deliveredOnly = false)
    {
        var s = await _db.StoreSettings.AsNoTracking().FirstOrDefaultAsync();
        var storeLat = s?.StoreLat ?? 0.0;
        var storeLng = s?.StoreLng ?? 0.0;

        var bikeSpeed = (double?)(s?.DriverSpeedBikeKmH) ?? 18.0;
        var carSpeed = (double?)(s?.DriverSpeedCarKmH) ?? 30.0;

        static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
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

        var drivers = await _db.Drivers.AsNoTracking().Select(d => new { d.Id, d.VehicleType }).ToListAsync();
        var driverMap = drivers.ToDictionary(x => x.Id, x => x.VehicleType);

        var raw = await _db.Orders.AsNoTracking()
            .Where(o => deliveredOnly ? o.CurrentStatus == OrderStatus.Delivered : o.CurrentStatus != OrderStatus.Delivered)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Select(o => new { o.Id, o.CustomerId, o.DriverId, o.CurrentStatus, o.Total, o.DeliveryFee, o.DeliveryDistanceKm, o.CreatedAtUtc, o.ProcessingEtaMinutes, o.DeliveryEtaMinutes, o.ExpectedDeliveryAtUtc, o.LastEtaUpdatedAtUtc, o.DeliveryLat, o.DeliveryLng })
            .ToListAsync();

        var orderIds = raw.Select(x => x.Id).ToList();

        var customerEditedIds = await _db.OrderStatusHistory.AsNoTracking()
            .Where(h => orderIds.Contains(h.OrderId) &&
                        (h.ReasonCode == "customer_edit" ||
                         (h.Comment != null && h.Comment.Contains("تم تعديل الطلب من قبل الزبون"))))
            .Select(h => h.OrderId)
            .Distinct()
            .ToListAsync();
        var customerEditedSet = customerEditedIds.ToHashSet();

        var adminEditedIds = await _db.OrderStatusHistory.AsNoTracking()
            .Where(h => orderIds.Contains(h.OrderId) &&
                        (h.ChangedByType == "admin" &&
                         (h.Comment != null && h.Comment.Contains("تم تعديل الطلب من قبل الإدارة"))))
            .Select(h => h.OrderId)
            .Distinct()
            .ToListAsync();
        var adminEditedSet = adminEditedIds.ToHashSet();

        var customerIds = raw.Select(x => x.CustomerId).Distinct().ToList();
        var custMap = await _db.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.Phone })
            .ToDictionaryAsync(c => c.Id, c => new { c.Name, c.Phone });

        var cancelMap = await _db.OrderStatusHistory.AsNoTracking()
            .Where(h => orderIds.Contains(h.OrderId) && h.Status == OrderStatus.Cancelled)
            .GroupBy(h => h.OrderId)
            .Select(g => g.OrderByDescending(x => x.ChangedAtUtc).Select(x => new { x.OrderId, x.Comment }).First())
            .ToDictionaryAsync(x => x.OrderId, x => (x.Comment ?? "").Trim());

        var orders = raw.Select(o =>
        {
            int? approxTravelEtaMinutes = null;
            if (storeLat != 0 && storeLng != 0 && o.DeliveryLat != 0 && o.DeliveryLng != 0)
            {
                var v = o.DriverId.HasValue && driverMap.TryGetValue(o.DriverId.Value, out var vt) ? vt : VehicleType.Car;
                var speedKmH = v == VehicleType.Bike ? bikeSpeed : carSpeed;
                if (speedKmH <= 0) speedKmH = 30.0;
                var km = HaversineKm(storeLat, storeLng, o.DeliveryLat, o.DeliveryLng);
                approxTravelEtaMinutes = (int)Math.Max(1, Math.Round((km / speedKmH) * 60.0));
            }
            return new
            {
                o.Id,
                o.CustomerId,
                customerName = custMap.TryGetValue(o.CustomerId, out var cust1) ? (cust1.Name ?? string.Empty) : string.Empty,
                customerPhone = custMap.TryGetValue(o.CustomerId, out var cust2) ? cust2.Phone : null,
                o.DriverId,
                o.CurrentStatus,
                o.Total,
                deliveryFee = o.DeliveryFee,
                deliveryDistanceKm = Math.Round(o.DeliveryDistanceKm, 3),
                o.CreatedAtUtc,
                o.ProcessingEtaMinutes,
                o.DeliveryEtaMinutes,
                o.ExpectedDeliveryAtUtc,
                o.LastEtaUpdatedAtUtc,

                deliveryLat = o.DeliveryLat,
                deliveryLng = o.DeliveryLng,
                approxTravelEtaMinutes,
                wasEditedByCustomer = customerEditedSet.Contains(o.Id),
                wasEditedByAdmin = adminEditedSet.Contains(o.Id),
                cancelLabel = cancelMap.TryGetValue(o.Id, out var cancelText) ? cancelText : null
            };
        }).ToList();

        return Ok(orders);
    }

    [HttpGet("order/{id:int}")]
    public async Task<IActionResult> GetOrder(int id)
    {
        var o = await _db.Orders.AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.StatusHistory)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (o == null) return NotFound(new { error = "not_found" });

        var cust = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == o.CustomerId);

        var productIds = o.Items.Where(i => i.ProductId > 0).Select(i => i.ProductId).Distinct().ToList();
        List<(int Id, int CategoryId, string CatName)> productCategoryMap;
        if (productIds.Count == 0)
        {
            productCategoryMap = new List<(int Id, int CategoryId, string CatName)>();
        }
        else
        {
            var raw = await _db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .Include(p => p.Category)
                .Select(p => new { p.Id, p.CategoryId, CatName = p.Category != null ? p.Category.Name : (string?)null })
                .ToListAsync();
            productCategoryMap = raw.Select(x => (x.Id, x.CategoryId, CatName: x.CatName ?? "")).ToList();
        }
        var productCategoryNames = productCategoryMap.ToDictionary(x => x.Id, x => x.CatName);
        var productCategoryIds = productCategoryMap.ToDictionary(x => x.Id, x => x.CategoryId);

        return Ok(new
        {
            o.Id,
            o.CustomerId,
            customerName = cust?.Name,
            customerPhone = cust?.Phone,
            o.CurrentStatus,
            o.Notes,
            o.DeliveryLat,
            o.DeliveryLng,
            o.DeliveryAddress,
            o.Subtotal,
            o.DeliveryFee,
            o.Total,
            createdAtUtc = o.CreatedAtUtc,
            deliveryDistanceKm = Math.Round(o.DeliveryDistanceKm, 3),
            orderType = o.DeliveryFee > 0 ? "delivery" : "pickup",
            paymentMethod = "نقدي",
            items = o.Items.Select(i => new
            {
                i.ProductId,
                i.ProductNameSnapshot,
                i.UnitPriceSnapshot,
                i.Quantity,
                i.OptionsSnapshot,
                categoryName = i.ProductId > 0 && productCategoryNames.TryGetValue(i.ProductId, out var cn) ? cn : (string?)null,
                categoryId = i.ProductId > 0 && productCategoryIds.TryGetValue(i.ProductId, out var cid) ? (int?)cid : (int?)null
            }),
            history = o.StatusHistory.OrderBy(h => h.ChangedAtUtc).Select(h => new { h.Status, h.ChangedByType, h.Comment, h.ChangedAtUtc })
        });
    }

    public record AdminEditOrderItemReq(int ProductId, int Quantity, string? OptionsSnapshot);
    public record AdminEditOrderRequest(List<AdminEditOrderItemReq> Items, string? Notes, double? DeliveryLat, double? DeliveryLng, string? DeliveryAddress, decimal? DeliveryFee);

    [HttpPost("order/{id:int}/edit")]
    public async Task<IActionResult> AdminEditOrder(int id, AdminEditOrderRequest req)
    {
        var o = await _db.Orders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (o == null) return NotFound(new { error = "not_found" });

        if (o.CurrentStatus == OrderStatus.Delivered || o.CurrentStatus == OrderStatus.Cancelled)
            return BadRequest(new { error = "not_editable", message = "لا يمكن تعديل هذا الطلب" });

        if (req.Items == null || req.Items.Count == 0)
            return BadRequest(new { error = "empty_items", message = "لا يمكن أن يكون الطلب فارغاً" });

        var productIds = req.Items.Where(i => i.ProductId > 0).Select(i => i.ProductId).Distinct().ToList();
        var offerIds = req.Items.Where(i => i.ProductId < 0).Select(i => Math.Abs(i.ProductId)).Distinct().ToList();

        var offerProductLinks = (offerIds.Count == 0)
            ? new List<OfferProduct>()
            : await _db.OfferProducts.AsNoTracking().Where(x => offerIds.Contains(x.OfferId)).ToListAsync();
        var offerPrimaryProduct = offerProductLinks
            .GroupBy(x => x.OfferId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ProductId).FirstOrDefault());

        var linkedProductIds = offerProductLinks.Select(x => x.ProductId).Distinct().ToList();
        foreach (var pid in linkedProductIds)
            if (!productIds.Contains(pid)) productIds.Add(pid);

        var products = (productIds.Count == 0)
            ? new List<Product>()
            : await _db.Products.Where(p => productIds.Contains(p.Id)).ToListAsync();

        var offers = (offerIds.Count == 0)
            ? new List<Offer>()
            : await _db.Offers.AsNoTracking().Where(x => offerIds.Contains(x.Id)).ToListAsync();

        var variants = (productIds.Count == 0)
            ? new List<ProductVariant>()
            : await _db.ProductVariants.AsNoTracking().Where(v => productIds.Contains(v.ProductId)).ToListAsync();
        var addons = (productIds.Count == 0)
            ? new List<ProductAddon>()
            : await _db.ProductAddons.AsNoTracking().Where(a => productIds.Contains(a.ProductId)).ToListAsync();

        var now = DateTime.UtcNow;
        var discounts = await _db.Discounts.AsNoTracking()
            .Where(d => d.IsActive
                        && d.TargetType != DiscountTargetType.Cart
                        && (d.StartsAtUtc == null || d.StartsAtUtc <= now)
                        && (d.EndsAtUtc == null || d.EndsAtUtc >= now))
            .ToListAsync();

        decimal ApplyDiscount(decimal original, Discount d)
        {
            if (original <= 0) return 0;
            decimal v = original;
            if (d.ValueType == DiscountValueType.Percent)
            {
                var p = d.Percent ?? 0;
                v = original - (original * p / 100m);
            }
            else
            {
                var a = d.Amount ?? 0;
                v = original - a;
            }
            if (v < 0) v = 0;
            return Math.Round(v, 2);
        }

        (decimal finalBasePrice, string? badgeText, decimal? percent) BestDiscountForProduct(int productId, int categoryId, decimal original)
        {
            if (discounts.Count == 0) return (original, null, null);
            var prod = discounts.Where(x => x.TargetType == DiscountTargetType.Product && x.TargetId == productId).ToList();
            var cat = discounts.Where(x => x.TargetType == DiscountTargetType.Category && x.TargetId == categoryId).ToList();
            Discount? best = null;
            decimal bestFinal = original;
            foreach (var d in prod.Concat(cat))
            {
                var f = ApplyDiscount(original, d);
                if (f < bestFinal)
                {
                    bestFinal = f;
                    best = d;
                }
            }
            if (best == null || bestFinal >= original) return (original, null, null);
            decimal? pct = null;
            if (best.ValueType == DiscountValueType.Percent) pct = best.Percent;
            else if (original > 0) pct = Math.Round((1m - (bestFinal / original)) * 100m, 0);
            var badge = !string.IsNullOrWhiteSpace(best.BadgeText) ? best.BadgeText : (pct != null ? $"خصم {pct}%" : "خصم");
            return (bestFinal, badge, pct);
        }

        var newItems = new List<OrderItem>();
        decimal subtotalAfter = 0;
        decimal subtotalBefore = 0;

        foreach (var it in req.Items)
        {
            if (it.Quantity <= 0) continue;

            if (it.ProductId > 0)
            {
                var p = products.FirstOrDefault(x => x.Id == it.ProductId);
                if (p == null) return BadRequest(new { error = "invalid_items", message = "بعض الأصناف غير صحيحة" });

                int? variantId = null;
                List<int> addonIds = new();
                string? noteText = null;
                if (!string.IsNullOrWhiteSpace(it.OptionsSnapshot))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(it.OptionsSnapshot);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("variantId", out var v1) && v1.ValueKind == JsonValueKind.Number) variantId = v1.GetInt32();
                        else if (root.TryGetProperty("VariantId", out var v2) && v2.ValueKind == JsonValueKind.Number) variantId = v2.GetInt32();

                        if (root.TryGetProperty("addonIds", out var a1) && a1.ValueKind == JsonValueKind.Array)
                            addonIds = a1.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt32()).ToList();
                        else if (root.TryGetProperty("AddonIds", out var a2) && a2.ValueKind == JsonValueKind.Array)
                            addonIds = a2.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt32()).ToList();

                        if (root.TryGetProperty("note", out var n1) && n1.ValueKind == JsonValueKind.String) noteText = n1.GetString();
                        else if (root.TryGetProperty("Note", out var n2) && n2.ValueKind == JsonValueKind.String) noteText = n2.GetString();
                    }
                    catch { }
                }

                var baseOriginal = p.Price;
                var d = BestDiscountForProduct(p.Id, p.CategoryId, baseOriginal);
                var baseAfter = d.finalBasePrice;

                decimal variantDelta = 0;
                string? variantName = null;
                if (variantId.HasValue)
                {
                    var v = variants.FirstOrDefault(x => x.Id == variantId.Value && x.ProductId == p.Id && x.IsActive);
                    if (v != null)
                    {
                        variantDelta = v.PriceDelta;
                        variantName = v.Name;
                    }
                }

                decimal addonsDelta = 0;
                var chosenAddons = new List<object>();
                foreach (var aid in addonIds.Distinct())
                {
                    var a = addons.FirstOrDefault(x => x.Id == aid && x.ProductId == p.Id && x.IsActive);
                    if (a == null) continue;
                    addonsDelta += a.Price;
                    chosenAddons.Add(new { a.Id, a.Name, a.Price });
                }

                var unitBefore = baseOriginal + variantDelta + addonsDelta;
                var unitAfter = baseAfter + variantDelta + addonsDelta;

                subtotalBefore += unitBefore * it.Quantity;
                subtotalAfter += unitAfter * it.Quantity;

                var snap = JsonSerializer.Serialize(new
                {
                    variantId,
                    variantName,
                    variantDelta,
                    addonIds = addonIds.Distinct().ToList(),
                    addons = chosenAddons,
                    note = string.IsNullOrWhiteSpace(noteText) ? null : noteText.Trim(),
                    discount = new { baseOriginal, baseAfter, percent = d.percent, badge = d.badgeText }
                });

                newItems.Add(new OrderItem
                {
                    OrderId = o.Id,
                    ProductId = p.Id,
                    ProductNameSnapshot = p.Name,
                    UnitPriceSnapshot = unitAfter,
                    Quantity = it.Quantity,
                    OptionsSnapshot = snap
                });
            }
            else
            {
                var offerId = Math.Abs(it.ProductId);
                var off = offers.FirstOrDefault(x => x.Id == offerId);
                if (off == null) return BadRequest(new { error = "invalid_items", message = "بعض العروض غير صحيحة" });

                int? templateProductId = offerPrimaryProduct.ContainsKey(offerId) ? offerPrimaryProduct[offerId] : (int?)null;
                int? variantId = null;
                List<int> addonIds = new();
                string? noteText = null;

                if (!string.IsNullOrWhiteSpace(it.OptionsSnapshot))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(it.OptionsSnapshot);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("variantId", out var v1) && v1.ValueKind == JsonValueKind.Number) variantId = v1.GetInt32();
                        else if (root.TryGetProperty("VariantId", out var v2) && v2.ValueKind == JsonValueKind.Number) variantId = v2.GetInt32();

                        if (root.TryGetProperty("addonIds", out var a1) && a1.ValueKind == JsonValueKind.Array)
                            addonIds = a1.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt32()).ToList();
                        else if (root.TryGetProperty("AddonIds", out var a2) && a2.ValueKind == JsonValueKind.Array)
                            addonIds = a2.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt32()).ToList();

                        if (root.TryGetProperty("note", out var n1) && n1.ValueKind == JsonValueKind.String) noteText = n1.GetString();
                        else if (root.TryGetProperty("Note", out var n2) && n2.ValueKind == JsonValueKind.String) noteText = n2.GetString();
                    }
                    catch { }
                }

                decimal variantDelta = 0;
                string? variantName = null;
                decimal addonsDelta = 0;
                var chosenAddons = new List<object>();

                if (templateProductId.HasValue && templateProductId.Value > 0)
                {
                    if (variantId.HasValue)
                    {
                        var v = variants.FirstOrDefault(x => x.Id == variantId.Value && x.ProductId == templateProductId.Value && x.IsActive);
                        if (v != null) { variantDelta = v.PriceDelta; variantName = v.Name; }
                    }
                    foreach (var aid in addonIds.Distinct())
                    {
                        var a = addons.FirstOrDefault(x => x.Id == aid && x.ProductId == templateProductId.Value && x.IsActive);
                        if (a == null) continue;
                        addonsDelta += a.Price;
                        chosenAddons.Add(new { a.Id, a.Name, a.Price });
                    }
                }

                decimal offerBasePrice = off.PriceAfter ?? off.PriceBefore ?? 0m;
                if (offerBasePrice <= 0m && templateProductId.HasValue && templateProductId.Value > 0)
                {
                    var tp = products.FirstOrDefault(p => p.Id == templateProductId.Value);
                    if (tp != null) offerBasePrice = tp.Price;
                }

                decimal unit = offerBasePrice + variantDelta + addonsDelta;
                subtotalBefore += unit * it.Quantity;
                subtotalAfter += unit * it.Quantity;

                var snap = JsonSerializer.Serialize(new
                {
                    isOffer = true,
                    offerId,
                    variantId,
                    variantName,
                    variantDelta,
                    addonIds = addonIds.Distinct().ToList(),
                    addons = chosenAddons,
                    note = string.IsNullOrWhiteSpace(noteText) ? null : noteText.Trim()
                });

                newItems.Add(new OrderItem
                {
                    OrderId = o.Id,
                    ProductId = -offerId,
                    ProductNameSnapshot = off.Title,
                    UnitPriceSnapshot = unit,
                    Quantity = it.Quantity,
                    OptionsSnapshot = snap
                });
            }
        }

        if (newItems.Count == 0)
            return BadRequest(new { error = "empty_items", message = "لا يمكن أن يكون الطلب فارغاً" });

        if (req.DeliveryLat != null && req.DeliveryLng != null)
        {
            var lat = req.DeliveryLat.Value;
            var lng = req.DeliveryLng.Value;
            if (lat < -90 || lat > 90 || lng < -180 || lng > 180)
                return BadRequest(new { error = "invalid_location", message = "الموقع غير صحيح" });
            o.DeliveryLat = lat;
            o.DeliveryLng = lng;
        }
        if (!string.IsNullOrWhiteSpace(req.DeliveryAddress)) o.DeliveryAddress = req.DeliveryAddress.Trim();
        if (req.DeliveryFee.HasValue) o.DeliveryFee = Math.Max(0, req.DeliveryFee.Value);

        o.Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim();
        o.Subtotal = subtotalAfter;
        o.TotalBeforeDiscount = subtotalBefore + o.DeliveryFee;
        o.CartDiscount = Math.Max(0, subtotalBefore - subtotalAfter);
        o.Total = subtotalAfter + o.DeliveryFee;

        o.StatusHistory.Add(new OrderStatusHistory
        {
            OrderId = o.Id,
            Status = o.CurrentStatus,
            ChangedAtUtc = DateTime.UtcNow,
            ChangedByType = "admin",
            Comment = "تم تعديل الطلب من قبل الإدارة"
        });

        _db.OrderItems.RemoveRange(o.Items);
        await _db.SaveChangesAsync();
        _db.OrderItems.AddRange(newItems);
        await _db.SaveChangesAsync();

        await _notifyHub.Clients.Group("admin").SendAsync("order_status", new { orderId = o.Id, status = o.CurrentStatus.ToString() });

        return Ok(new { ok = true, orderId = o.Id, o.Subtotal, o.DeliveryFee, o.Total });
    }

    [HttpGet("customers")]
    public async Task<IActionResult> ListCustomers([FromQuery] string? search)
    {
        var query = _db.Customers.AsNoTracking().OrderByDescending(c => c.Id);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(c =>
                (c.Name != null && c.Name.Contains(term)) ||
                (c.Phone != null && c.Phone.Contains(term))).OrderByDescending(c => c.Id);
        }
        var list = await query
            .Select(c => new {
                c.Id,
                c.Name,
                c.Phone,
                c.Email,
                c.DefaultAddress,
                c.DefaultLat,
                c.DefaultLng,
                c.LastLat,
                c.LastLng,
                c.IsChatBlocked,
                c.IsAppBlocked,
                c.CreatedAtUtc
            })
            .ToListAsync();
        return Ok(new { customers = list });
    }

    [HttpGet("chat-thread/{customerId:int}")]
    public async Task<IActionResult> GetOrCreateChatThread(int customerId)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId);
        if (customer == null) return NotFound(new { error = "not_found" });

        var now = DateTime.UtcNow;
        var thread = await _db.ComplaintThreads
            .OrderByDescending(t => t.UpdatedAtUtc)
            .FirstOrDefaultAsync(t => t.CustomerId == customerId);

        if (thread == null)
        {
            thread = new ComplaintThread
            {
                CustomerId = customerId,
                OrderId = null,
                Title = "دردشة مع المتجر",
                UpdatedAtUtc = now,
                CreatedAtUtc = now,
                LastAdminSeenAtUtc = now
            };
            _db.ComplaintThreads.Add(thread);
            await _db.SaveChangesAsync();

            await _notifyHub.Clients.Group("admin").SendAsync("complaint_new", new { thread.Id, thread.Title, thread.CustomerId, thread.OrderId });
        }

        return Ok(new { threadId = thread.Id, customerId, customerName = customer.Name, isChatBlocked = customer.IsChatBlocked });
    }

    public record ChatBlockReq(bool Blocked);

    [HttpPost("customers/{customerId:int}/chat-block")]
    public async Task<IActionResult> SetCustomerChatBlock(int customerId, ChatBlockReq req)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId);
        if (customer == null) return NotFound(new { error = "not_found" });

        customer.IsChatBlocked = req.Blocked;
        await _db.SaveChangesAsync();

        var payload = new { customerId, isChatBlocked = customer.IsChatBlocked };
        await _notifyHub.Clients.Group("admin").SendAsync("chat_blocked", payload);
        await _notifyHub.Clients.Group($"customer-{customerId}").SendAsync("chat_blocked", payload);

        return Ok(new { ok = true, customerId, isChatBlocked = customer.IsChatBlocked });
    }

    public record AppBlockReq(bool Blocked);

    [HttpPost("customers/{customerId:int}/app-block")]
    public async Task<IActionResult> SetCustomerAppBlock(int customerId, AppBlockReq req)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId);
        if (customer == null) return NotFound(new { error = "not_found" });

        customer.IsAppBlocked = req.Blocked;
        await _db.SaveChangesAsync();

        var payload = new { customerId, isAppBlocked = customer.IsAppBlocked };
        await _notifyHub.Clients.Group("admin").SendAsync("app_blocked", payload);
        await _notifyHub.Clients.Group($"customer-{customerId}").SendAsync("app_blocked", payload);

        return Ok(new { ok = true, customerId, isAppBlocked = customer.IsAppBlocked });
    }

    [HttpDelete("customers/{id:int}")]
    public async Task<IActionResult> DeleteCustomer(int id)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id);
        if (customer == null) return NotFound(new { error = "not_found" });

        await _notifyHub.Clients.Group($"customer-{id}").SendAsync("account_deleted", new { customerId = id });

        var orderIds = await _db.Orders.Where(o => o.CustomerId == id).Select(o => o.Id).ToListAsync();

        foreach (var orderId in orderIds)
        {
            var o = await _db.Orders.Include(x => x.Items).Include(x => x.StatusHistory).FirstOrDefaultAsync(x => x.Id == orderId);
            if (o == null) continue;
            var or = await _db.OrderRatings.FirstOrDefaultAsync(r => r.OrderId == orderId);
            if (or != null) _db.OrderRatings.Remove(or);
            var threads = await _db.ComplaintThreads.Where(t => t.OrderId == orderId).ToListAsync();
            foreach (var t in threads)
            {
                var msgs = await _db.ComplaintMessages.Where(m => m.ThreadId == t.Id).ToListAsync();
                _db.ComplaintMessages.RemoveRange(msgs);
            }
            _db.ComplaintThreads.RemoveRange(threads);
            _db.OrderStatusHistory.RemoveRange(o.StatusHistory);
            _db.OrderItems.RemoveRange(o.Items);
            _db.Orders.Remove(o);
        }

        var customerThreads = await _db.ComplaintThreads.Where(t => t.CustomerId == id).ToListAsync();
        foreach (var t in customerThreads)
        {
            var msgs = await _db.ComplaintMessages.Where(m => m.ThreadId == t.Id).ToListAsync();
            _db.ComplaintMessages.RemoveRange(msgs);
        }
        _db.ComplaintThreads.RemoveRange(customerThreads);

        var ratings = await _db.Ratings.Where(r => r.CustomerId == id).ToListAsync();
        _db.Ratings.RemoveRange(ratings);

        var addresses = await _db.CustomerAddresses.Where(a => a.CustomerId == id).ToListAsync();
        _db.CustomerAddresses.RemoveRange(addresses);

        var notifs = await _db.Notifications.Where(n => n.UserType == NotificationUserType.Customer && n.UserId == id).ToListAsync();
        _db.Notifications.RemoveRange(notifs);

        var tokens = await _db.DeviceTokens.Where(t => t.UserType == DeviceUserType.Customer && t.UserId == id).ToListAsync();
        _db.DeviceTokens.RemoveRange(tokens);

        _db.Customers.Remove(customer);
        await _db.SaveChangesAsync();

        return Ok(new { ok = true });
    }

    [HttpGet("customers/{customerId:int}/details")]
    public async Task<IActionResult> GetCustomerDetails(int customerId)
    {
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId);
        if (customer == null) return NotFound();

        var orders = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Take(50)
            .ToListAsync();

        var orderIds = orders.Select(o => o.Id).ToList();
        var ratings = await _db.OrderRatings
            .AsNoTracking()
            .Where(r => orderIds.Contains(r.OrderId))
            .ToListAsync();
        var ratingByOrder = ratings.ToDictionary(r => r.OrderId, r => r);

        var shapedOrders = orders.Select(o => new
        {
            id = o.Id,
            status = o.CurrentStatus.ToString(),
            total = o.Total,
            subtotal = o.Subtotal,
            deliveryFee = o.DeliveryFee,
            cartDiscount = o.CartDiscount,
            totalBeforeDiscount = o.TotalBeforeDiscount,
            createdAtUtc = o.CreatedAtUtc,
            editableUntilUtc = o.OrderEditableUntilUtc,
            notes = o.Notes,
            deliveryAddress = o.DeliveryAddress,
            items = o.Items.Select(i => new
            {
                id = i.Id,
                productId = i.ProductId,
                name = i.ProductNameSnapshot,
                qty = i.Quantity,
                unit = i.UnitPriceSnapshot,
                options = i.OptionsSnapshot
            }),
            orderRating = ratingByOrder.TryGetValue(o.Id, out var rr) ? new
            {
                storeRate = rr.StoreRate,
                driverRate = rr.DriverRate,
                comment = rr.Comment,
                createdAtUtc = rr.CreatedAtUtc
            } : null
        });

        return Ok(new
        {
            customer = new
            {
                id = customer.Id,
                name = customer.Name,
                phone = customer.Phone,
                createdAtUtc = customer.CreatedAtUtc,
                isChatBlocked = customer.IsChatBlocked
            },
            orders = shapedOrders
        });
    }

    [HttpGet("reports/summary")]
    public async Task<IActionResult> ReportsSummary()
    {
        try
        {
            var utcNow = DateTime.UtcNow;
            var today = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, 0, 0, 0, DateTimeKind.Utc);
            var tomorrow = today.AddDays(1);

            // 1) العمولات المسجلة اليوم (تشمل طلبات مقبولة + مسلّمة)
            var todayCommissions = await _db.AgentCommissions.AsNoTracking()
                .Include(c => c.Agent)
                .Where(c => c.CreatedAtUtc >= today && c.CreatedAtUtc < tomorrow)
                .ToListAsync();

            // 2) الطلبات المكتملة أو النشطة اليوم (تشمل Delivered وكل حالات غير الملغي)
            var allTodayOrderIds = await _db.Orders.AsNoTracking()
                .Where(o => o.CurrentStatus != OrderStatus.Cancelled &&
                    (o.DeliveredAtUtc.HasValue
                        ? (o.DeliveredAtUtc >= today && o.DeliveredAtUtc < tomorrow)
                        : (o.CreatedAtUtc >= today && o.CreatedAtUtc < tomorrow)))
                .Select(o => o.Id)
                .ToListAsync();

            var deliveredTodayIds = await _db.Orders.AsNoTracking()
                .Where(o => o.CurrentStatus == OrderStatus.Delivered &&
                    (o.DeliveredAtUtc.HasValue
                        ? (o.DeliveredAtUtc >= today && o.DeliveredAtUtc < tomorrow)
                        : (o.CreatedAtUtc >= today && o.CreatedAtUtc < tomorrow)))
                .Select(o => o.Id)
                .ToListAsync();

            // 3) دمج: طلبات العمولات + جميع طلبات اليوم
            var commissionOrderIds = todayCommissions.Select(c => c.OrderId).Distinct().ToList();
            var allOrderIds = allTodayOrderIds.Union(commissionOrderIds).Distinct().ToList();

            // 4) جلب بيانات الطلبات الموحدة
            decimal salesToday = 0, salesTodaySubtotal = 0, deliveryFeesToday = 0;
            int ordersCount = 0;
            double? avgPrep = null, avgDel = null;

            if (allOrderIds.Count > 0)
            {
                var orders = await _db.Orders.AsNoTracking()
                    .Where(o => allOrderIds.Contains(o.Id))
                    .Select(o => new {
                        o.Id,
                        o.Total,
                        o.Subtotal,
                        o.DeliveryFee,
                        o.ProcessingEtaMinutes,
                        o.DeliveryEtaMinutes
                    })
                    .ToListAsync();

                ordersCount = orders.Count;
                salesToday = orders.Sum(o => o.Total);
                salesTodaySubtotal = orders.Sum(o => o.Subtotal > 0 ? o.Subtotal : o.Total);
                deliveryFeesToday = orders.Sum(o => o.DeliveryFee);

                var prepTimes = orders.Where(o => o.ProcessingEtaMinutes.HasValue)
                                      .Select(o => (double)o.ProcessingEtaMinutes!.Value).ToList();
                var delTimes = orders.Where(o => o.DeliveryEtaMinutes.HasValue)
                                      .Select(o => (double)o.DeliveryEtaMinutes!.Value).ToList();
                if (prepTimes.Count > 0) avgPrep = prepTimes.Average();
                if (delTimes.Count > 0) avgDel = delTimes.Average();
            }

            // 5) أفضل المنتجات
            var topProducts = new List<object>();
            if (allOrderIds.Count > 0)
            {
                var rawItems = await _db.OrderItems.AsNoTracking()
                    .Where(oi => allOrderIds.Contains(oi.OrderId))
                    .Select(x => new { x.ProductNameSnapshot, x.Quantity, unitPrice = (double)x.UnitPriceSnapshot })
                    .ToListAsync();

                topProducts = rawItems
                    .GroupBy(x => x.ProductNameSnapshot)
                    .Select(g => (object)new
                    {
                        name = g.Key,
                        qty = g.Sum(x => x.Quantity),
                        revenue = (decimal)g.Sum(x => x.unitPrice * x.Quantity)
                    })
                    .OrderByDescending(x => ((dynamic)x).qty)
                    .Take(10)
                    .ToList();
            }

            // 6) عمولات المندوبين
            var settings = await _db.StoreSettings.AsNoTracking().FirstOrDefaultAsync();
            var storeName = settings?.StoreName?.Trim() ?? "متجرنا";

            List<AgentCommissionRow> agentCommissions;
            if (todayCommissions.Count > 0)
            {
                agentCommissions = todayCommissions
                    .GroupBy(c => new { c.AgentId, Name = c.Agent?.Name ?? $"#{c.AgentId}" })
                    .Select(g => new AgentCommissionRow(
                        agentId: g.Key.AgentId,
                        agentName: g.Key.Name,
                        orderCount: g.Count(),
                        totalSales: g.Sum(c => c.SaleAmount),
                        totalCommission: g.Sum(c => c.CommissionAmount),
                        netToAgent: g.Sum(c => c.SaleAmount - c.CommissionAmount)
                    ))
                    .OrderByDescending(x => x.totalSales)
                    .ToList();
            }
            else if (allOrderIds.Count > 0)
            {
                var agentItems = await _db.OrderAgentItems.AsNoTracking()
                    .Include(ai => ai.Agent)
                    .Where(ai => allOrderIds.Contains(ai.OrderId))
                    .ToListAsync();

                agentCommissions = agentItems
                    .GroupBy(ai => new { ai.AgentId, Name = ai.Agent?.Name ?? $"#{ai.AgentId}" })
                    .Select(g =>
                    {
                        var items = g.ToList();
                        var totalSales = items.Sum(ai => ai.AgentSubtotal > 0 ? ai.AgentSubtotal : 0m);
                        var totalComm = items.Sum(ai =>
                        {
                            var pct = ai.CommissionPercent > 0 ? ai.CommissionPercent : (ai.Agent?.CommissionPercent ?? 0m);
                            var sale = ai.AgentSubtotal > 0 ? ai.AgentSubtotal : 0m;
                            return Math.Round(sale * pct / 100m, 2);
                        });
                        return new AgentCommissionRow(
                            agentId: g.Key.AgentId,
                            agentName: g.Key.Name,
                            orderCount: items.Count,
                            totalSales: totalSales,
                            totalCommission: totalComm,
                            netToAgent: totalSales - totalComm
                        );
                    }).ToList();
            }
            else { agentCommissions = new List<AgentCommissionRow>(); }

            var agentCommTotalToday = agentCommissions.Sum(x => x.totalCommission);
            var storeProfitToday = Math.Round(salesTodaySubtotal - agentCommTotalToday, 2);

            return Ok(new
            {
                salesToday,
                ordersCount,
                salesTodaySubtotal,
                agentCommTotalToday,
                storeProfitToday,
                avgProcessingEtaMinutes = avgPrep,
                avgDeliveryEtaMinutes = avgDel,
                topProducts,
                agentCommissions,
                storeName
            });
        }
        catch (Exception ex)
        {
            return Ok(new
            {
                salesToday = 0m,
                ordersCount = 0,
                salesTodaySubtotal = 0m,
                agentCommTotalToday = 0m,
                storeProfitToday = 0m,
                avgProcessingEtaMinutes = (double?)null,
                avgDeliveryEtaMinutes = (double?)null,
                topProducts = new List<object>(),
                agentCommissions = new List<object>(),
                storeName = "متجرنا",
                error = ex.Message
            });
        }
    }

    [HttpGet("reports/weekly-summary")]
    public async Task<IActionResult> ReportsWeeklySummary()
    {
        try
        {
            var now = DateTime.UtcNow;
            var end = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc).AddDays(1);
            var start = end.AddDays(-7);
            var payload = await BuildRangeSummary(start, end);
            return Ok(payload);
        }
        catch (Exception ex)
        {
            return Ok(new
            {
                sales = 0m,
                ordersCount = 0,
                storeProfit = 0m,
                agentCommissionsTotal = 0m,
                topProducts = new List<object>(),
                agentCommissions = new List<object>(),
                error = ex.Message
            });
        }
    }

    [HttpGet("reports/monthly-summary")]
    public async Task<IActionResult> ReportsMonthlySummary()
    {
        try
        {
            var now = DateTime.UtcNow;
            var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = start.AddMonths(1);
            var payload = await BuildRangeSummary(start, end);
            return Ok(payload);
        }
        catch (Exception ex)
        {
            return Ok(new
            {
                sales = 0m,
                ordersCount = 0,
                storeProfit = 0m,
                agentCommissionsTotal = 0m,
                topProducts = new List<object>(),
                agentCommissions = new List<object>(),
                error = ex.Message
            });
        }
    }

    private async Task<object> BuildRangeSummary(DateTime startUtc, DateTime endUtc)
    {
        var deliveredOrders = await _db.Orders.AsNoTracking()
            .Where(o => o.CurrentStatus != OrderStatus.Cancelled
                        && (o.DeliveredAtUtc.HasValue ? (o.DeliveredAtUtc >= startUtc && o.DeliveredAtUtc < endUtc) : (o.CreatedAtUtc >= startUtc && o.CreatedAtUtc < endUtc)))
            .Select(o => new
            {
                o.Id,
                o.Total,
                o.Subtotal,
                o.DeliveryFee,
                o.DriverId,
                o.DriverConfirmedAtUtc,
                o.DeliveredAtUtc,
                o.DistanceKm,
                o.CreatedAtUtc
            })
            .ToListAsync();

        // عمولات المندوبين للفترة - نقرأ من AgentCommissions المرتبطة بطلبات الفترة مباشرة
        var orderIdsInRange = deliveredOrders.Select(o => o.Id).ToList();
        decimal agentCommissionsTotal = 0m;
        if (orderIdsInRange.Count > 0)
        {
            // جلب البيانات أولاً ثم الحساب في الـ memory (SQLite لا يدعم Sum على decimal مباشرة)
            var commAmounts = await _db.AgentCommissions.AsNoTracking()
                .Where(c => orderIdsInRange.Contains(c.OrderId))
                .Select(c => (double)c.CommissionAmount)
                .ToListAsync();
            agentCommissionsTotal = commAmounts.Count > 0 ? (decimal)commAmounts.Sum() : 0m;

            // fallback إذا لم تُسجَّل عمولات بعد
            if (agentCommissionsTotal == 0m)
            {
                var agentItemsInRange = await _db.OrderAgentItems.AsNoTracking()
                    .Include(ai => ai.Agent)
                    .Where(ai => orderIdsInRange.Contains(ai.OrderId))
                    .ToListAsync();
                agentCommissionsTotal = agentItemsInRange.Sum(ai =>
                {
                    var pct = ai.CommissionPercent > 0 ? ai.CommissionPercent : (ai.Agent?.CommissionPercent ?? 0m);
                    var sale = ai.AgentSubtotal > 0 ? ai.AgentSubtotal : 0m;
                    return Math.Round(sale * pct / 100m, 2);
                });
            }
        }

        var ordersCount = deliveredOrders.Count;
        var sales = deliveredOrders.Sum(o => o.Total);
        var totalSubtotal = deliveredOrders.Sum(o => o.Subtotal > 0 ? o.Subtotal : o.Total);
        var totalDeliveryFees = deliveredOrders.Sum(o => o.DeliveryFee);

        double? avgDeliveryMinutes = null;
        var withTimes = deliveredOrders.Where(o => o.DriverConfirmedAtUtc != null && o.DeliveredAtUtc != null).ToList();
        if (withTimes.Count > 0)
            avgDeliveryMinutes = withTimes.Average(x => (x.DeliveredAtUtc!.Value - x.DriverConfirmedAtUtc!.Value).TotalMinutes);

        var daily = deliveredOrders
            .GroupBy(o => (o.DeliveredAtUtc ?? o.CreatedAtUtc).Date)
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                dateUtc = g.Key,
                ordersCount = g.Count(),
                sales = g.Sum(x => x.Total)
            })
            .ToList();

        var topProductsRaw = await (from oi in _db.OrderItems.AsNoTracking()
                                    join o in _db.Orders.AsNoTracking() on oi.OrderId equals o.Id
                                    where o.CurrentStatus != OrderStatus.Cancelled
                                          && (o.DeliveredAtUtc.HasValue ? (o.DeliveredAtUtc >= startUtc && o.DeliveredAtUtc < endUtc) : (o.CreatedAtUtc >= startUtc && o.CreatedAtUtc < endUtc))
                                    select new
                                    {
                                        name = oi.ProductNameSnapshot,
                                        qty = oi.Quantity,
                                        unitPrice = (double)oi.UnitPriceSnapshot
                                    })
            .ToListAsync();

        var topProducts = topProductsRaw
            .GroupBy(x => x.name)
            .Select(g => new
            {
                name = g.Key,
                qty = g.Sum(x => x.qty),
                revenue = (decimal)g.Sum(x => x.unitPrice * x.qty)
            })
            .OrderByDescending(x => x.revenue)
            .Take(10)
            .Cast<object>()
            .ToList();

        var byDriver = deliveredOrders
            .Where(o => o.DriverId != null)
            .GroupBy(o => o.DriverId!.Value)
            .Select(g => new
            {
                driverId = g.Key,
                deliveredCount = g.Count(),
                totalAmount = g.Sum(x => x.Total),
                totalDeliveryFees = g.Sum(x => x.DeliveryFee),
                totalDistanceKm = g.Sum(x => Math.Max(0.0, x.DistanceKm)),
                avgDeliveryMinutes = g.Where(x => x.DriverConfirmedAtUtc != null && x.DeliveredAtUtc != null)
                    .Select(x => (x.DeliveredAtUtc!.Value - x.DriverConfirmedAtUtc!.Value).TotalMinutes)
                    .DefaultIfEmpty()
                    .Average()
            })
            .OrderByDescending(x => x.totalAmount)
            .Take(10)
            .ToList();

        var driverIds = byDriver.Select(x => x.driverId).ToList();
        var drivers = await _db.Drivers.AsNoTracking()
            .Where(d => driverIds.Contains(d.Id))
            .Select(d => new { d.Id, d.Name, d.Phone, d.CommissionPercent })
            .ToListAsync();
        var driverMap = drivers.ToDictionary(d => d.Id);

        var topDrivers = byDriver.Select(x => new
        {
            x.driverId,
            driverName = driverMap.TryGetValue(x.driverId, out var d) ? d.Name : $"#{x.driverId}",
            driverPhone = driverMap.TryGetValue(x.driverId, out var d2) ? d2.Phone : "",
            x.deliveredCount,
            x.totalAmount,
            x.totalDeliveryFees,
            commissionPercent = driverMap.TryGetValue(x.driverId, out var d3) ? d3.CommissionPercent : 0m,
            driverEarnings = Math.Round(x.totalDeliveryFees * (driverMap.TryGetValue(x.driverId, out var d4) ? d4.CommissionPercent : 0m) / 100m, 2),
            x.totalDistanceKm,
            x.avgDeliveryMinutes
        }).ToList();

        var totalDriverEarnings = topDrivers.Sum(x => (decimal)x.driverEarnings);
        var storeProfit = Math.Round(totalSubtotal - agentCommissionsTotal, 2);

        // عمولات المندوبين التفصيلية للفترة - نقرأ بـ OrderId مباشرة
        List<AgentCommissionRow> agentCommissions;
        if (orderIdsInRange.Count > 0)
        {
            var agentCommissionsRaw = await _db.AgentCommissions.AsNoTracking()
                .Include(c => c.Agent)
                .Where(c => orderIdsInRange.Contains(c.OrderId))
                .ToListAsync();

            if (agentCommissionsRaw.Count > 0)
            {
                agentCommissions = agentCommissionsRaw
                    .GroupBy(c => new { c.AgentId, Name = c.Agent?.Name ?? $"#{c.AgentId}" })
                    .Select(g => new AgentCommissionRow(
                        agentId: g.Key.AgentId,
                        agentName: g.Key.Name,
                        orderCount: g.Count(),
                        totalSales: g.Sum(c => c.SaleAmount),
                        totalCommission: g.Sum(c => c.CommissionAmount),
                        netToAgent: g.Sum(c => c.SaleAmount - c.CommissionAmount)
                    ))
                    .OrderByDescending(x => x.totalSales)
                    .ToList();
            }
            else
            {
                var agentItemsFallback = await _db.OrderAgentItems.AsNoTracking()
                    .Include(ai => ai.Agent)
                    .Where(ai => orderIdsInRange.Contains(ai.OrderId))
                    .ToListAsync();

                agentCommissions = agentItemsFallback
                    .GroupBy(ai => new { ai.AgentId, Name = ai.Agent?.Name ?? $"#{ai.AgentId}" })
                    .Select(g =>
                    {
                        var items = g.ToList();
                        var totalSales = items.Sum(ai => ai.AgentSubtotal > 0 ? ai.AgentSubtotal : 0m);
                        var totalComm = items.Sum(ai =>
                        {
                            var pct = ai.CommissionPercent > 0 ? ai.CommissionPercent : (ai.Agent?.CommissionPercent ?? 0m);
                            var sale = ai.AgentSubtotal > 0 ? ai.AgentSubtotal : 0m;
                            return Math.Round(sale * pct / 100m, 2);
                        });
                        return new AgentCommissionRow(
                            agentId: g.Key.AgentId,
                            agentName: g.Key.Name,
                            orderCount: items.Count,
                            totalSales: totalSales,
                            totalCommission: totalComm,
                            netToAgent: totalSales - totalComm
                        );
                    })
                    .OrderByDescending(x => x.totalSales)
                    .ToList();
            }
        }
        else
        {
            agentCommissions = new List<AgentCommissionRow>();
        }

        var settings2 = await _db.StoreSettings.AsNoTracking().FirstOrDefaultAsync();
        var storeName = settings2?.StoreName?.Trim() ?? "متجرنا";

        return new
        {
            startUtc,
            endUtc,
            sales,
            ordersCount,
            totalSubtotal,
            totalDeliveryFees,
            totalDriverEarnings = Math.Round(totalDriverEarnings, 2),
            agentCommissionsTotal = Math.Round(agentCommissionsTotal, 2),
            storeProfit = Math.Round(storeProfit, 2),
            avgDeliveryEtaMinutes = avgDeliveryMinutes,
            daily,
            topProducts,
            topDrivers,
            agentCommissions,
            storeName
        };
    }

    [HttpGet("reports/products-daily")]
    public async Task<IActionResult> ReportsProductsDaily()
    {
        var now = DateTime.UtcNow;
        var today = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var tomorrow = today.AddDays(1);

        var q = from oi in _db.OrderItems.AsNoTracking()
                join o in _db.Orders.AsNoTracking() on oi.OrderId equals o.Id
                where o.CurrentStatus == OrderStatus.Delivered
                      && (o.DeliveredAtUtc.HasValue ? (o.DeliveredAtUtc >= today && o.DeliveredAtUtc < tomorrow) : (o.CreatedAtUtc >= today && o.CreatedAtUtc < tomorrow))
                select new { oi.ProductNameSnapshot, unitPrice = (double)oi.UnitPriceSnapshot, oi.Quantity };

        var rawRows = await q.ToListAsync();

        var rows = rawRows
            .GroupBy(x => x.ProductNameSnapshot)
            .Select(g => new
            {
                name = g.Key,
                qty = g.Sum(x => x.Quantity),
                revenue = (decimal)g.Sum(x => x.unitPrice * x.Quantity)
            })
            .OrderByDescending(x => x.revenue)
            .ToList();

        return Ok(rows);
    }

    private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0;
        static double ToRad(double deg) => deg * (Math.PI / 180.0);
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    [HttpGet("reports/drivers-daily")]
    public async Task<IActionResult> ReportsDriversDaily()
    {
        var now = DateTime.UtcNow;
        var today = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var tomorrow = today.AddDays(1);

        var s = await _db.StoreSettings.AsNoTracking().FirstOrDefaultAsync();
        var rLat = s?.StoreLat ?? 0.0;
        var rLng = s?.StoreLng ?? 0.0;

        var delivered = await _db.Orders.AsNoTracking()
            .Where(o => o.DriverId != null && o.CurrentStatus == OrderStatus.Delivered && (o.DeliveredAtUtc.HasValue ? (o.DeliveredAtUtc >= today && o.DeliveredAtUtc < tomorrow) : (o.CreatedAtUtc >= today && o.CreatedAtUtc < tomorrow)))
            .Select(o => new { o.Id, o.DriverId, o.Total, o.Subtotal, o.DeliveryFee, o.DistanceKm, o.DriverConfirmedAtUtc, o.DeliveredAtUtc, o.DeliveryLat, o.DeliveryLng })
            .ToListAsync();

        var driverIds = delivered.Select(x => x.DriverId!.Value).Distinct().ToList();
        var drivers = await _db.Drivers.AsNoTracking()
            .Where(d => driverIds.Contains(d.Id))
            .Select(d => new { d.Id, d.Name, d.Phone })
            .ToListAsync();
        var byId = drivers.ToDictionary(d => d.Id);

        var driverCommMap = await _db.Drivers.AsNoTracking()
            .Where(d => driverIds.Contains(d.Id))
            .Select(d => new { d.Id, d.CommissionPercent })
            .ToListAsync();
        var driverCommDict = driverCommMap.ToDictionary(d => d.Id, d => d.CommissionPercent);

        var rows = delivered
            .GroupBy(o => o.DriverId!.Value)
            .Select(g =>
            {
                var d = byId.TryGetValue(g.Key, out var dd) ? dd : new { Id = g.Key, Name = $"#{g.Key}", Phone = "" };
                var actualDist = g.Sum(x => Math.Max(0.0, x.DistanceKm));
                var withTimes = g.Where(x => x.DriverConfirmedAtUtc != null && x.DeliveredAtUtc != null).ToList();
                double? avgMin = null;
                if (withTimes.Count > 0)
                    avgMin = withTimes.Average(x => (x.DeliveredAtUtc!.Value - x.DriverConfirmedAtUtc!.Value).TotalMinutes);
                var totalDeliveryFees = g.Sum(x => x.DeliveryFee);
                var pct = driverCommDict.TryGetValue(g.Key, out var cp) ? cp : 0m;
                var driverEarnings = Math.Round(totalDeliveryFees * pct / 100m, 2);
                return new
                {
                    driverId = g.Key,
                    driverName = d.Name,
                    driverPhone = d.Phone,
                    deliveredCount = g.Count(),
                    totalAmount = g.Sum(x => x.Total),
                    totalDeliveryFees,
                    commissionPercent = pct,
                    driverEarnings,
                    avgDeliveryMinutes = avgMin,
                    totalDistanceKm = Math.Round(actualDist, 3)
                };
            })
            .OrderByDescending(x => x.deliveredCount)
            .ThenByDescending(x => x.totalAmount)
            .ToList();

        return Ok(rows);
    }

    [HttpGet("reports/top")]
    public async Task<IActionResult> ReportsTop()
    {
        var now = DateTime.UtcNow;
        var today = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var tomorrow = today.AddDays(1);

        var topProducts = await _db.OrderItems.AsNoTracking()
            .Where(oi => _db.Orders.Any(o => o.Id == oi.OrderId
                                            && o.CurrentStatus == OrderStatus.Delivered
                                            && (o.DeliveredAtUtc.HasValue ? (o.DeliveredAtUtc >= today && o.DeliveredAtUtc < tomorrow) : (o.CreatedAtUtc >= today && o.CreatedAtUtc < tomorrow))))
            .GroupBy(oi => oi.ProductNameSnapshot)
            .Select(g => new { name = g.Key, qty = g.Sum(x => x.Quantity), revenue = g.Sum(x => x.UnitPriceSnapshot * x.Quantity) })
            .OrderByDescending(x => x.revenue)
            .Take(10)
            .ToListAsync();

        var deliveredOrders = await _db.Orders.AsNoTracking()
            .Where(o => o.DriverId != null
                        && o.CurrentStatus == OrderStatus.Delivered
                        && (o.DeliveredAtUtc.HasValue ? (o.DeliveredAtUtc >= today && o.DeliveredAtUtc < tomorrow) : (o.CreatedAtUtc >= today && o.CreatedAtUtc < tomorrow)))
            .Select(o => new { o.DriverId, o.Total, o.DistanceKm, o.DriverConfirmedAtUtc, o.DeliveredAtUtc })
            .ToListAsync();

        var topDrivers = deliveredOrders
            .GroupBy(o => o.DriverId!.Value)
            .Select(g =>
            {
                var withTimes = g.Where(x => x.DriverConfirmedAtUtc != null && x.DeliveredAtUtc != null).ToList();
                double? avgMin = null;
                if (withTimes.Count > 0)
                    avgMin = withTimes.Average(x => (x.DeliveredAtUtc!.Value - x.DriverConfirmedAtUtc!.Value).TotalMinutes);
                var distKm = g.Sum(x => Math.Max(0.0, x.DistanceKm));
                return new { driverId = g.Key, deliveredCount = g.Count(), totalAmount = g.Sum(x => x.Total), avgDeliveryMinutes = avgMin, totalDistanceKm = Math.Round(distKm, 3) };
            })
            .OrderByDescending(x => x.deliveredCount)
            .ThenByDescending(x => x.totalAmount)
            .Take(10)
            .ToList();

        var ids = topDrivers.Select(x => x.driverId).ToList();
        var names = await _db.Drivers.AsNoTracking().Where(d => ids.Contains(d.Id)).Select(d => new { d.Id, d.Name }).ToListAsync();
        var map = names.ToDictionary(x => x.Id, x => x.Name);
        var topDriversNamed = topDrivers.Select(x => new { x.driverId, driverName = map.TryGetValue(x.driverId, out var n) ? n : $"#{x.driverId}", x.deliveredCount, x.totalAmount, x.avgDeliveryMinutes, x.totalDistanceKm }).ToList();

        return Ok(new { topProducts, topDrivers = topDriversNamed });
    }

    public record AssignDriverReq(int OrderId, int? DriverId);

    public record AssignDriverBulkReq(List<int> OrderIds, int DriverId);

    [HttpPost("assign-driver")]
    public async Task<IActionResult> AssignDriver(AssignDriverReq req)
    {
        var o = await _db.Orders.FirstOrDefaultAsync(x => x.Id == req.OrderId);
        if (o == null) return NotFound(new { error = "not_found" });

        if (o.CurrentStatus == OrderStatus.Delivered || o.CurrentStatus == OrderStatus.Cancelled)
            return BadRequest(new { error = "invalid_status" });

        o.DriverId = req.DriverId;
        if (req.DriverId != null)
        {

            const int maxActiveOrdersPerDriver = 15;
            var activeCount = await _db.Orders.AsNoTracking().CountAsync(x =>
                x.DriverId == req.DriverId &&
                x.Id != o.Id &&
                x.CurrentStatus != OrderStatus.Delivered &&
                x.CurrentStatus != OrderStatus.Cancelled);
            if (activeCount >= maxActiveOrdersPerDriver)
                return BadRequest(new { error = "driver_active_limit" });

            var driver = await _db.Drivers.AsNoTracking().FirstOrDefaultAsync(d => d.Id == req.DriverId);
            var driverName = driver?.Name ?? $"#{req.DriverId}";

            o.CurrentStatus = OrderStatus.ReadyForPickup;
            _db.OrderStatusHistory.Add(new OrderStatusHistory { OrderId = o.Id, Status = o.CurrentStatus, Comment = $"تم تعيين السائق: {driverName}", ChangedByType = "admin" });
            await _notifyHub.Clients.Group($"driver-{req.DriverId}").SendAsync("order_assigned", new { orderId = o.Id });

            await _notifications.CreateAndBroadcastAsync(NotificationUserType.Admin, null,
                "تم تعيين سائق", $"تم تعيين السائق {driverName} للطلب #{o.Id}", o.Id);

            await _notifications.CreateAndBroadcastAsync(NotificationUserType.Customer, o.CustomerId,
                "تم تعيين سائق لطلبك 🚗", $"تم تعيين السائق {driverName} لتوصيل طلبك رقم #{o.Id}.", o.Id);
        }

        await _db.SaveChangesAsync();
        await _notifyHub.Clients.Group("admin").SendAsync("order_assigned", new { orderId = o.Id, driverId = o.DriverId });
        await _notifyHub.Clients.Group($"customer-{o.CustomerId}").SendAsync("order_status", new { orderId = o.Id, status = o.CurrentStatus, driverId = o.DriverId });

        // ✅ إرسال تحديث للمندوبين المرتبطين بهذا الطلب
        var agentPayload = new { orderId = o.Id, status = o.CurrentStatus, driverId = o.DriverId };
        var assignedAgentIds = await _db.OrderAgentItems
            .Where(ai => ai.OrderId == o.Id && ai.AgentStatus != AgentOrderStatus.Rejected)
            .Select(ai => ai.AgentId)
            .ToListAsync();
        foreach (var aId in assignedAgentIds)
            await _notifyHub.Clients.Group($"agent-{aId}").SendAsync("order_status", agentPayload);

        var payload = new { ok = true, orderId = o.Id, status = o.CurrentStatus, driverId = o.DriverId };
        return Ok(payload);
    }

    [HttpPost("order/{id:int}/cancel")]
    public async Task<IActionResult> AdminCancelOrder(int id)
    {
        var o = await _db.Orders
            .Include(x => x.StatusHistory)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (o == null) return NotFound(new { error = "not_found" });
        if (o.CurrentStatus == OrderStatus.Delivered || o.CurrentStatus == OrderStatus.Cancelled)
            return BadRequest(new { error = "cannot_cancel", message = "لا يمكن إلغاء هذا الطلب" });

        o.CurrentStatus = OrderStatus.Cancelled;

        var customerText = "تم إلغاء طلبك من قبل إدارة المتجر";
        o.CancelReasonCode = customerText.Length <= 80 ? customerText : customerText[..80];

        _db.OrderStatusHistory.Add(new OrderStatusHistory
        {
            OrderId = o.Id,
            Status = OrderStatus.Cancelled,
            ReasonCode = "admin_cancel",
            Comment = "تم إلغاء الطلب من قبل الإدارة",
            ChangedByType = "admin",
            ChangedAtUtc = DateTime.UtcNow
        });

        if (o.DriverId.HasValue)
        {
            var d = await _db.Drivers.FindAsync(o.DriverId.Value);
            if (d != null) d.Status = DriverStatus.Available;
        }

        await _db.SaveChangesAsync();

        var payload = new { orderId = o.Id, status = o.CurrentStatus, driverId = o.DriverId };
        await _notifyHub.Clients.Group("admin").SendAsync("order_status", payload);
        await _notifyHub.Clients.Group($"customer-{o.CustomerId}").SendAsync("order_status", payload);
        await _notifyHub.Clients.Group("admin").SendAsync("order_status_changed", payload);
        await _notifyHub.Clients.Group($"customer-{o.CustomerId}").SendAsync("order_status_changed", payload);
        if (o.DriverId != null)
            await _notifyHub.Clients.Group($"driver-{o.DriverId}").SendAsync("order_status", payload);

        await _notifications.CreateAndBroadcastAsync(NotificationUserType.Admin, null,
            "إلغاء طلب", $"تم إلغاء الطلب #{o.Id} من قبل الإدارة", o.Id);

        await _notifications.CreateAndBroadcastAsync(NotificationUserType.Customer, o.CustomerId,
            "تم إلغاء طلبك", customerText, o.Id);

        await _notifications.SendCustomerOrderStatusPushIfNeededAsync(o.CustomerId, o.Id, o.CurrentStatus, o.ProcessingEtaMinutes, o.DeliveryEtaMinutes);

        return Ok(new { ok = true });
    }

    [HttpDelete("order/{id:int}/delete")]
    public async Task<IActionResult> DeleteOrder(int id)
    {
        var o = await _db.Orders
            .Include(x => x.Items)
            .Include(x => x.StatusHistory)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (o == null) return NotFound(new { error = "not_found" });

        _db.OrderItems.RemoveRange(o.Items);

        _db.OrderStatusHistory.RemoveRange(o.StatusHistory);

        var rating = await _db.OrderRatings.FirstOrDefaultAsync(r => r.OrderId == id);
        if (rating != null)
            _db.OrderRatings.Remove(rating);

        var complaintThreads = await _db.ComplaintThreads.Where(c => c.OrderId == id).ToListAsync();
        if (complaintThreads.Any())
        {
            foreach (var thread in complaintThreads)
            {
                var messages = await _db.ComplaintMessages.Where(m => m.ThreadId == thread.Id).ToListAsync();
                _db.ComplaintMessages.RemoveRange(messages);
            }
            _db.ComplaintThreads.RemoveRange(complaintThreads);
        }

        _db.Orders.Remove(o);

        if (o.DriverId.HasValue)
        {
            var d = await _db.Drivers.FindAsync(o.DriverId.Value);
            if (d != null && d.Status == DriverStatus.Busy)
            {

                var hasOtherOrders = await _db.Orders
                    .AnyAsync(x => x.DriverId == o.DriverId.Value &&
                                   x.Id != id &&
                                   x.CurrentStatus != OrderStatus.Delivered &&
                                   x.CurrentStatus != OrderStatus.Cancelled);
                if (!hasOtherOrders)
                    d.Status = DriverStatus.Available;
            }
        }

        await _db.SaveChangesAsync();

        await _notifyHub.Clients.Group("admin").SendAsync("order_deleted", new { orderId = id });

        return Ok(new { ok = true });
    }

    [HttpDelete("orders/delete-all")]
    public async Task<IActionResult> DeleteAllOrders()
    {

        await _db.Database.ExecuteSqlRawAsync("DELETE FROM ComplaintMessages WHERE ThreadId IN (SELECT Id FROM ComplaintThreads WHERE OrderId IS NOT NULL)");

        await _db.Database.ExecuteSqlRawAsync("DELETE FROM ComplaintThreads WHERE OrderId IS NOT NULL");

        await _db.Database.ExecuteSqlRawAsync("DELETE FROM OrderRatings");

        await _db.Database.ExecuteSqlRawAsync("DELETE FROM OrderStatusHistory");

        await _db.Database.ExecuteSqlRawAsync("DELETE FROM OrderItems");

        await _db.Database.ExecuteSqlRawAsync("DELETE FROM Orders");

        var busyDrivers = await _db.Drivers.Where(d => d.Status == DriverStatus.Busy).ToListAsync();
        foreach (var driver in busyDrivers)
        {
            driver.Status = DriverStatus.Available;
        }

        await _db.SaveChangesAsync();

        await _notifyHub.Clients.Group("admin").SendAsync("orders_deleted_all", new { });

        return Ok(new { ok = true });
    }

    [HttpPost("assign-driver/bulk")]
    public async Task<IActionResult> AssignDriverBulk(AssignDriverBulkReq req)
    {
        if (req.OrderIds == null || req.OrderIds.Count == 0)
            return BadRequest(new { error = "empty" });

        var driver = await _db.Drivers.AsNoTracking().FirstOrDefaultAsync(d => d.Id == req.DriverId);
        if (driver == null) return BadRequest(new { error = "invalid_driver" });

        var driverName = driver.Name ?? $"#{req.DriverId}";

        var distinct = req.OrderIds.Distinct().ToList();
        const int maxActiveOrdersPerDriver = 15;
        var activeCount = await _db.Orders.AsNoTracking().CountAsync(x =>
            x.DriverId == req.DriverId &&
            x.CurrentStatus != OrderStatus.Delivered &&
            x.CurrentStatus != OrderStatus.Cancelled);

        if (activeCount + distinct.Count > maxActiveOrdersPerDriver)
            return BadRequest(new { error = "driver_active_limit" });

        var orders = await _db.Orders.Where(o => distinct.Contains(o.Id)).ToListAsync();
        if (orders.Count == 0) return NotFound(new { error = "not_found" });

        var assignedCount = 0;
        foreach (var o in orders)
        {
            if (o.CurrentStatus == OrderStatus.Delivered || o.CurrentStatus == OrderStatus.Cancelled) continue;
            o.DriverId = req.DriverId;
            o.CurrentStatus = OrderStatus.ReadyForPickup;
            _db.OrderStatusHistory.Add(new OrderStatusHistory { OrderId = o.Id, Status = o.CurrentStatus, Comment = $"تم تعيين السائق (دفعة واحدة): {driverName}", ChangedByType = "admin" });
            await _notifyHub.Clients.Group($"driver-{req.DriverId}").SendAsync("order_assigned", new { orderId = o.Id });
            await _notifyHub.Clients.Group($"customer-{o.CustomerId}").SendAsync("order_status", new { orderId = o.Id, status = o.CurrentStatus, driverId = o.DriverId });
            await _notifications.CreateAndBroadcastAsync(NotificationUserType.Admin, null,
                "تم تعيين سائق", $"تم تعيين السائق {driverName} للطلب #{o.Id}", o.Id);
            assignedCount++;
        }

        await _db.SaveChangesAsync();

        await _notifyHub.Clients.Group("admin").SendAsync("order_assigned", new { bulk = true, driverId = req.DriverId });
        return Ok(new { ok = true, assigned = assignedCount });
    }

    public record UpdateOrderStatusReq(int OrderId, OrderStatus Status, string? Comment);

    public record ManualOrderItemReq(int ProductId, int Quantity, string? OptionsSnapshot);

    public record ManualOrderRequest(
        string CustomerName,
        string CustomerPhone,
        string? DeliveryAddress,
        decimal DeliveryFee,
        string? Notes,
        List<ManualOrderItemReq> Items
    );

    [HttpPost("manual-order")]
    public async Task<IActionResult> CreateManualOrder(ManualOrderRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.CustomerName))
            return BadRequest(new { message = "يرجى إدخال اسم الزبون" });

        if (string.IsNullOrWhiteSpace(req.CustomerPhone))
            return BadRequest(new { message = "يرجى إدخال رقم هاتف الزبون" });

        if (req.Items == null || req.Items.Count == 0)
            return BadRequest(new { message = "يرجى إضافة صنف واحد على الأقل" });

        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Phone == req.CustomerPhone);

        if (customer == null)
        {
            customer = new Entities.Customer
            {
                Name = req.CustomerName,
                Phone = req.CustomerPhone,
                DefaultAddress = req.DeliveryAddress ?? "",
            };
            _db.Customers.Add(customer);
            await _db.SaveChangesAsync();
        }

        var productIds = req.Items.Where(i => i.ProductId > 0).Select(i => i.ProductId).Distinct().ToList();
        var products = await _db.Products
            .Where(p => productIds.Contains(p.Id) && p.IsActive)
            .ToListAsync();

        if (products.Count != productIds.Count)
            return BadRequest(new { message = "بعض الأصناف غير موجودة أو غير نشطة" });

        var variants = (productIds.Count == 0)
            ? new List<ProductVariant>()
            : await _db.ProductVariants.AsNoTracking()
                .Where(v => productIds.Contains(v.ProductId) && v.IsActive)
                .ToListAsync();

        var addons = (productIds.Count == 0)
            ? new List<ProductAddon>()
            : await _db.ProductAddons.AsNoTracking()
                .Where(a => productIds.Contains(a.ProductId) && a.IsActive)
                .ToListAsync();

        var orderItems = new List<Entities.OrderItem>();
        decimal subtotal = 0;

        foreach (var item in req.Items)
        {
            if (item.ProductId <= 0 || item.Quantity <= 0) continue;

            var prod = products.First(p => p.Id == item.ProductId);

            int? variantId = null;
            List<int> addonIds = new();
            string? noteText = null;

            if (!string.IsNullOrWhiteSpace(item.OptionsSnapshot))
            {
                try
                {
                    using var doc = JsonDocument.Parse(item.OptionsSnapshot);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("variantId", out var v1) && v1.ValueKind == JsonValueKind.Number) variantId = v1.GetInt32();
                    else if (root.TryGetProperty("VariantId", out var v2) && v2.ValueKind == JsonValueKind.Number) variantId = v2.GetInt32();

                    if (root.TryGetProperty("addonIds", out var a1) && a1.ValueKind == JsonValueKind.Array)
                        addonIds = a1.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt32()).ToList();
                    else if (root.TryGetProperty("AddonIds", out var a2) && a2.ValueKind == JsonValueKind.Array)
                        addonIds = a2.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt32()).ToList();

                    if (root.TryGetProperty("note", out var n1) && n1.ValueKind == JsonValueKind.String) noteText = n1.GetString();
                    else if (root.TryGetProperty("Note", out var n2) && n2.ValueKind == JsonValueKind.String) noteText = n2.GetString();
                }
                catch
                {

                }
            }

            decimal variantDelta = 0;
            string? variantName = null;
            if (variantId.HasValue)
            {
                var v = variants.FirstOrDefault(x => x.Id == variantId.Value && x.ProductId == prod.Id && x.IsActive);
                if (v != null)
                {
                    variantDelta = v.PriceDelta;
                    variantName = v.Name;
                }
            }

            decimal addonsDelta = 0;
            var chosenAddons = new List<object>();
            foreach (var aid in addonIds.Distinct())
            {
                var a = addons.FirstOrDefault(x => x.Id == aid && x.ProductId == prod.Id && x.IsActive);
                if (a == null) continue;
                addonsDelta += a.Price;
                chosenAddons.Add(new { a.Id, a.Name, a.Price });
            }

            var unitPrice = prod.Price + variantDelta + addonsDelta;
            subtotal += unitPrice * item.Quantity;

            var snap = JsonSerializer.Serialize(new
            {
                variantId,
                variantName,
                variantDelta,
                addonIds = addonIds.Distinct().ToList(),
                addons = chosenAddons,
                note = noteText
            });

            orderItems.Add(new Entities.OrderItem
            {
                ProductId = prod.Id,
                ProductNameSnapshot = prod.Name,
                UnitPriceSnapshot = unitPrice,
                Quantity = item.Quantity,
                OptionsSnapshot = snap
            });
        }

        var deliveryFee = req.DeliveryFee > 0 ? req.DeliveryFee : 0m;
        var total = subtotal + deliveryFee;

        var order = new Entities.Order
        {
            CustomerId = customer.Id,
            CurrentStatus = Entities.OrderStatus.New,
            DeliveryAddress = req.DeliveryAddress ?? "",
            DeliveryLat = 0,
            DeliveryLng = 0,
            Notes = req.Notes ?? "",
            Subtotal = subtotal,
            DeliveryFee = deliveryFee,
            TotalBeforeDiscount = total,
            CartDiscount = 0,
            Total = total,
            IdempotencyKey = Guid.NewGuid().ToString(),
            Items = orderItems,
        };

        order.StatusHistory.Add(new Entities.OrderStatusHistory
        {
            Status = Entities.OrderStatus.New,
            ChangedByType = "admin",
            Comment = "طلب يدوي من لوحة التحكم",
        });

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        await _notifyHub.Clients.Group("admin").SendAsync("order_new", new { orderId = order.Id });

        return Ok(new { id = order.Id, ok = true });
    }

    public record UpdateOrderEtaReq(int OrderId, int? ProcessingEtaMinutes, int? DeliveryEtaMinutes);

    [HttpPost("order-eta")]
    public async Task<IActionResult> UpdateOrderEta(UpdateOrderEtaReq req)
    {
        var o = await _db.Orders.FirstOrDefaultAsync(x => x.Id == req.OrderId);
        if (o == null) return NotFound(new { error = "not_found" });

        o.ProcessingEtaMinutes = req.ProcessingEtaMinutes;
        o.DeliveryEtaMinutes = req.DeliveryEtaMinutes;

        var totalMinutes = (req.ProcessingEtaMinutes ?? 0) + (req.DeliveryEtaMinutes ?? 0);
        if (totalMinutes > 0)
            o.ExpectedDeliveryAtUtc = DateTime.UtcNow.AddMinutes(totalMinutes);
        else
            o.ExpectedDeliveryAtUtc = null;

        o.LastEtaUpdatedAtUtc = DateTime.UtcNow;
        _db.OrderStatusHistory.Add(new OrderStatusHistory
        {
            OrderId = o.Id,
            Status = o.CurrentStatus,
            ChangedByType = "admin",
            Comment = $"تم تحديث الوقت المتوقع: تحضير={req.ProcessingEtaMinutes ?? 0}د، توصيل={req.DeliveryEtaMinutes ?? 0}د"
        });

        await _db.SaveChangesAsync();

        await _notifyHub.Clients.Group("admin").SendAsync("eta_badge", new { orderId = o.Id });

        var prep = req.ProcessingEtaMinutes ?? 0;
        var del = req.DeliveryEtaMinutes ?? 0;
        await _notifications.CreateAndBroadcastAsync(NotificationUserType.Admin, null,
            "تحديث الوقت المتوقع", $"تم تحديث ETA للطلب #{o.Id}", o.Id);
        await _notifications.CreateAndBroadcastAsync(NotificationUserType.Customer, o.CustomerId,
            "تحديث الوقت المتوقع", $"تم تحديد الوقت المتوقع ✅ (تحضير: {prep} د، توصيل: {del} د)", o.Id);
        if (o.DriverId != null)
        {
            await _notifications.CreateAndBroadcastAsync(NotificationUserType.Driver, o.DriverId,
                "تحديث الوقت المتوقع", $"تم تحديث ETA للطلب #{o.Id}", o.Id);
        }
        await _notifications.SendCustomerEtaUpdatedPushAsync(o.CustomerId, o.Id, req.ProcessingEtaMinutes, req.DeliveryEtaMinutes);

        var payload = new
        {
            orderId = o.Id,
            prepEtaMinutes = o.ProcessingEtaMinutes,
            deliveryEtaMinutes = o.DeliveryEtaMinutes,
            expectedDeliveryAtUtc = o.ExpectedDeliveryAtUtc,
            lastEtaUpdatedAtUtc = o.LastEtaUpdatedAtUtc
        };

        await _notifyHub.Clients.Group("admin").SendAsync("order_eta", payload);
        await _notifyHub.Clients.Group($"customer-{o.CustomerId}").SendAsync("order_eta", payload);
        if (o.DriverId != null)
            await _notifyHub.Clients.Group($"driver-{o.DriverId}").SendAsync("order_eta", payload);

        return Ok(payload);
    }

    [HttpPost("order-status")]
    public async Task<IActionResult> UpdateOrderStatus(UpdateOrderStatusReq req)
    {
        var o = await _db.Orders.FirstOrDefaultAsync(x => x.Id == req.OrderId);
        if (o == null) return NotFound(new { error = "not_found" });
        if (o.CurrentStatus == OrderStatus.Delivered || o.CurrentStatus == OrderStatus.Cancelled)
            return BadRequest(new { error = "final_status" });

        var previous = o.CurrentStatus;
        o.CurrentStatus = req.Status;

        // ── تحديث حالة المندوب تلقائياً عند قبول الطلب من لوحة التحكم ──
        // إذا انتقل الطلب من New/Confirmed إلى Accepted/Preparing/ReadyForPickup
        // نحوّل AgentStatus من Pending إلى AutoAccepted حتى يظهر الطلب في قائمة النشطة
        if (req.Status >= OrderStatus.Accepted && req.Status < OrderStatus.Delivered)
        {
            var pendingAgentItems = await _db.OrderAgentItems
                .Where(ai => ai.OrderId == o.Id && ai.AgentStatus == AgentOrderStatus.Pending)
                .ToListAsync();
            foreach (var ai in pendingAgentItems)
            {
                ai.AgentStatus = AgentOrderStatus.AutoAccepted;
                ai.RespondedAtUtc = DateTime.UtcNow;

                // ✅ تسجيل العمولة عند القبول من الإدارة
                var existingComm = await _db.AgentCommissions.AnyAsync(c => c.OrderId == o.Id && c.AgentId == ai.AgentId);
                if (!existingComm)
                {
                    var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Id == ai.AgentId);
                    var commPct = ai.CommissionPercent > 0 ? ai.CommissionPercent : (agent?.CommissionPercent ?? 0m);
                    var saleAmt = ai.AgentSubtotal > 0 ? ai.AgentSubtotal : o.Total;
                    var commAmt = Math.Round(saleAmt * commPct / 100m, 2);

                    _db.AgentCommissions.Add(new AgentCommission
                    {
                        AgentId = ai.AgentId,
                        OrderId = o.Id,
                        SaleAmount = saleAmt,
                        CommissionPercent = commPct,
                        CommissionAmount = commAmt,
                        CreatedAtUtc = DateTime.UtcNow
                    });
                }

                // ✅ إرسال إشعار للمندوب
                await _notifyHub.Clients.Group($"agent-{ai.AgentId}")
                    .SendAsync("pending_order_accepted", new { orderId = o.Id });
            }
        }

        if (req.Status == OrderStatus.Delivered && o.DeliveredAtUtc == null)
        {
            o.DeliveredAtUtc = DateTime.UtcNow;

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

        if (req.Status == OrderStatus.WithDriver && o.DriverConfirmedAtUtc == null)
            o.DriverConfirmedAtUtc = DateTime.UtcNow;

        _db.OrderStatusHistory.Add(new OrderStatusHistory
        {
            OrderId = o.Id,
            Status = req.Status,
            Comment = string.IsNullOrWhiteSpace(req.Comment)
                ? (previous == req.Status ? null : $"{previous} -> {req.Status}")
                : req.Comment,
            ChangedByType = "admin"
        });
        await _db.SaveChangesAsync();
        var payload = new { orderId = o.Id, status = o.CurrentStatus, driverId = o.DriverId };
        await _notifyHub.Clients.Group("admin").SendAsync("order_status", payload);
        await _notifyHub.Clients.Group($"customer-{o.CustomerId}").SendAsync("order_status", payload);

        await _notifyHub.Clients.Group("admin").SendAsync("order_status_changed", payload);
        await _notifyHub.Clients.Group($"customer-{o.CustomerId}").SendAsync("order_status_changed", payload);

        // ✅ إرسال تحديث الحالة لجميع المندوبين المرتبطين بهذا الطلب
        var agentIds = await _db.OrderAgentItems
            .Where(ai => ai.OrderId == o.Id && ai.AgentStatus != AgentOrderStatus.Rejected)
            .Select(ai => ai.AgentId)
            .ToListAsync();
        foreach (var aId in agentIds)
            await _notifyHub.Clients.Group($"agent-{aId}").SendAsync("order_status", payload);

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
        return Ok(payload);
    }

    [HttpGet("complaints")]
    public async Task<IActionResult> ListComplaints()
    {
        var threads = await _db.ComplaintThreads.AsNoTracking()
            .Where(t => !t.IsArchivedByAdmin)
            .Select(t => new
            {
                t.Id,
                t.Title,
                t.CustomerId,
                t.OrderId,
                t.CreatedAtUtc,
                t.UpdatedAtUtc,
                t.LastAdminSeenAtUtc,
                customerName = _db.Customers.Where(c => c.Id == t.CustomerId).Select(c => c.Name).FirstOrDefault(),
                customerPhone = _db.Customers.Where(c => c.Id == t.CustomerId).Select(c => c.Phone).FirstOrDefault(),
                isChatBlocked = _db.Customers.Where(c => c.Id == t.CustomerId).Select(c => c.IsChatBlocked).FirstOrDefault(),
                lastMsg = _db.ComplaintMessages
                    .Where(m => m.ThreadId == t.Id)
                    .OrderByDescending(m => m.CreatedAtUtc)
                    .Select(m => new { m.FromAdmin, m.Message, m.CreatedAtUtc })
                    .FirstOrDefault(),
                unreadCount = _db.ComplaintMessages
                    .Where(m => m.ThreadId == t.Id && !m.FromAdmin && (t.LastAdminSeenAtUtc == null || m.CreatedAtUtc > t.LastAdminSeenAtUtc))
                    .Count()
            })
            .OrderByDescending(x => x.lastMsg != null ? x.lastMsg.CreatedAtUtc : x.UpdatedAtUtc)
            .ToListAsync();

        var list = threads.Select(x => new
        {
            x.Id,
            x.Title,
            x.CustomerId,
            customerName = x.customerName ?? "",
            customerPhone = x.customerPhone ?? "",
            isChatBlocked = x.isChatBlocked,
            x.OrderId,
            x.CreatedAtUtc,
            x.UpdatedAtUtc,
            unreadCount = x.unreadCount,
            lastMessagePreview = x.lastMsg == null ? "" : (x.lastMsg.FromAdmin ? "الإدارة: " : "الزبون: ") + (x.lastMsg.Message.Length > 60 ? x.lastMsg.Message.Substring(0, 60) + "…" : x.lastMsg.Message),
            lastMessageAtUtc = x.lastMsg?.CreatedAtUtc
        }).ToList();

        return Ok(list);
    }

    [HttpPost("complaint/{threadId:int}/archive")]
    public async Task<IActionResult> ArchiveComplaint(int threadId)
    {
        var t = await _db.ComplaintThreads.FirstOrDefaultAsync(x => x.Id == threadId);
        if (t == null) return NotFound(new { error = "not_found" });
        t.IsArchivedByAdmin = true;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpGet("complaint/{threadId:int}")]
    public async Task<IActionResult> GetComplaintThread(int threadId)
    {
        var t = await _db.ComplaintThreads
            .Include(x => x.Messages)
            .FirstOrDefaultAsync(x => x.Id == threadId);
        if (t == null) return NotFound(new { error = "not_found" });

        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == t.CustomerId);

        t.LastAdminSeenAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new
        {
            t.Id,
            t.Title,
            t.OrderId,
            t.CustomerId,
            customerName = customer?.Name ?? "",
            customerPhone = customer?.Phone ?? "",
            isChatBlocked = customer?.IsChatBlocked == true,
            messages = t.Messages.OrderBy(m => m.CreatedAtUtc)
                .Select(m => new { m.Id, fromAdmin = m.FromAdmin, message = m.Message, m.CreatedAtUtc })
        });
    }

    public record AdminReplyReq(string Message);

    [HttpPost("complaint/{threadId:int}/reply")]
    public async Task<IActionResult> ReplyComplaint(int threadId, AdminReplyReq req)
    {
        var t = await _db.ComplaintThreads.FirstOrDefaultAsync(x => x.Id == threadId);
        if (t == null) return NotFound(new { error = "not_found" });
        var now = DateTime.UtcNow;
        var msg = new ComplaintMessage { ThreadId = t.Id, FromAdmin = true, Message = req.Message };
        _db.ComplaintMessages.Add(msg);
        t.UpdatedAtUtc = now;
        await _db.SaveChangesAsync();

        var payload = new { id = msg.Id, threadId = t.Id, fromAdmin = true, message = req.Message, createdAtUtc = msg.CreatedAtUtc };
        await _notifyHub.Clients.Group($"customer-{t.CustomerId}").SendAsync("chat_message_received", payload);
        await _notifyHub.Clients.Group("admin").SendAsync("chat_message_received", payload);

        var snippet = (req.Message ?? "").Trim();
        if (snippet.Length > 80) snippet = snippet.Substring(0, 80) + "…";
        await _notifications.CreateAndBroadcastAsync(NotificationUserType.Customer, t.CustomerId,
            "رسالة جديدة", snippet, t.OrderId);

        await _notifications.SendCustomerChatPushAsync(t.CustomerId, t.OrderId, req.Message);

        return Ok(payload);
    }

    public record BroadcastChatReq(string Message);

    [HttpPost("broadcast-chat")]
    public async Task<IActionResult> BroadcastChat(BroadcastChatReq req)
    {
        var message = (req.Message ?? "").Trim();
        if (string.IsNullOrEmpty(message)) return BadRequest(new { error = "message_required" });

        var now = DateTime.UtcNow;
        var customerIds = await _db.Customers.Select(c => c.Id).ToListAsync();
        if (customerIds.Count == 0) return Ok(new { sent = 0, message = "لا يوجد زبائن" });

        var existingThreads = await _db.ComplaintThreads
            .Where(t => customerIds.Contains(t.CustomerId))
            .ToDictionaryAsync(t => t.CustomerId, t => t);

        foreach (var cid in customerIds)
        {
            if (!existingThreads.TryGetValue(cid, out var thread))
            {
                thread = new ComplaintThread
                {
                    CustomerId = cid,
                    OrderId = null,
                    Title = "دردشة مع المتجر",
                    UpdatedAtUtc = now,
                    CreatedAtUtc = now,
                    LastAdminSeenAtUtc = now
                };
                _db.ComplaintThreads.Add(thread);
                existingThreads[cid] = thread;
            }
        }
        await _db.SaveChangesAsync();

        foreach (var cid in customerIds)
        {
            var thread = existingThreads[cid];
            _db.ComplaintMessages.Add(new ComplaintMessage { ThreadId = thread.Id, FromAdmin = true, Message = message, CreatedAtUtc = now });
        }
        await _db.SaveChangesAsync();

        foreach (var cid in customerIds)
        {
            var thread = existingThreads[cid];
            var payload = new { id = 0, threadId = thread.Id, fromAdmin = true, message, createdAtUtc = now };
            await _notifyHub.Clients.Group($"customer-{cid}").SendAsync("chat_message_received", payload);
        }
        await _notifyHub.Clients.Group("admin").SendAsync("chat_message_received", new { broadcast = true, count = customerIds.Count });

        var snippet = message.Length > 80 ? message.Substring(0, 80) + "…" : message;

        return Ok(new { sent = customerIds.Count });
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings()
    {
        var s = await _db.StoreSettings.FirstAsync();

        return Ok(new
        {
            storeName = s.StoreName,
            logoUrl = s.LogoUrl,
            customerSplashUrl = s.CustomerSplashUrl,
            driverSplashUrl = s.DriverSplashUrl,
            splashBackground1Url = s.SplashBackground1Url,
            splashBackground2Url = s.SplashBackground2Url,
            primaryColorHex = s.PrimaryColorHex,
            secondaryColorHex = s.SecondaryColorHex,
            offersColorHex = s.OffersColorHex,
            welcomeText = s.WelcomeText,
            onboardingJson = s.OnboardingJson,
            homeBannersJson = s.HomeBannersJson,
            workHours = s.WorkHours,
            storeLat = s.StoreLat,
            storeLng = s.StoreLng,
            minOrderAmount = s.MinOrderAmount,
            deliveryFeeType = (int)s.DeliveryFeeType,
            deliveryFeeValue = s.DeliveryFeeValue,
            deliveryFeePerKm = s.DeliveryFeePerKm,
            supportPhone = s.SupportPhone,
            supportWhatsApp = s.SupportWhatsApp,
            closedMessage = s.ClosedMessage,
            closedScreenImageUrl = s.ClosedScreenImageUrl,
            isManuallyClosed = s.IsManuallyClosed,
            isAcceptingOrders = s.IsAcceptingOrders,
            routingProfile = s.RoutingProfile,
            driverSpeedBikeKmH = s.DriverSpeedBikeKmH,
            driverSpeedCarKmH = s.DriverSpeedCarKmH,
            facebookUrl = s.FacebookUrl,
            instagramUrl = s.InstagramUrl,
            telegramUrl = s.TelegramUrl,
            aiAutoReplyEnabled = s.AiAutoReplyEnabled,
            aiAutoReplySystemPrompt = s.AiAutoReplySystemPrompt,
            updatedAtUtc = s.UpdatedAtUtc
        });
    }


    public record UpdateSettingsReq(
        string? StoreName,
        string? LogoUrl,
        string? ClosedMessage,
        string? ClosedScreenImageUrl,
        string? CustomerSplashUrl,
        string? DriverSplashUrl,
        string? SplashBackground1Url,
        string? SplashBackground2Url,
        string? PrimaryColorHex,
        string? SecondaryColorHex,
        string? OffersColorHex,
        string? WelcomeText,
        string? OnboardingJson,
        string? HomeBannersJson,
        string? WorkHours,
        double? StoreLat,
        double? StoreLng,
        decimal? MinOrderAmount,
        DeliveryFeeType? DeliveryFeeType,
        decimal? DeliveryFeeValue,
        decimal? DeliveryFeePerKm,
        string? SupportPhone,
        string? SupportWhatsApp,
        string? FacebookUrl,
        string? InstagramUrl,
        string? TelegramUrl,
        bool? IsManuallyClosed,
        bool? IsAcceptingOrders,
        string? RoutingProfile,
        decimal? DriverSpeedBikeKmH,
        decimal? DriverSpeedCarKmH,
        bool? AiAutoReplyEnabled,
        string? AiAutoReplySystemPrompt
    );

    [HttpPost("settings")]
    public async Task<IActionResult> UpdateSettings(UpdateSettingsReq req)
    {
        var s = await _db.StoreSettings.FirstAsync();

        if (!string.IsNullOrWhiteSpace(req.StoreName)) s.StoreName = req.StoreName!.Trim();
        if (req.LogoUrl != null) s.LogoUrl = req.LogoUrl;
        if (req.ClosedMessage != null) s.ClosedMessage = req.ClosedMessage;
        if (req.ClosedScreenImageUrl != null) s.ClosedScreenImageUrl = req.ClosedScreenImageUrl;
        if (req.CustomerSplashUrl != null) s.CustomerSplashUrl = req.CustomerSplashUrl;
        if (req.DriverSplashUrl != null) s.DriverSplashUrl = req.DriverSplashUrl;
        if (req.SplashBackground1Url != null) s.SplashBackground1Url = req.SplashBackground1Url;
        if (req.SplashBackground2Url != null) s.SplashBackground2Url = req.SplashBackground2Url;
        if (!string.IsNullOrWhiteSpace(req.PrimaryColorHex)) s.PrimaryColorHex = req.PrimaryColorHex!.Trim();
        if (!string.IsNullOrWhiteSpace(req.SecondaryColorHex)) s.SecondaryColorHex = req.SecondaryColorHex!.Trim();
        if (!string.IsNullOrWhiteSpace(req.OffersColorHex)) s.OffersColorHex = req.OffersColorHex!.Trim();
        if (req.WelcomeText != null) s.WelcomeText = req.WelcomeText;
        if (req.OnboardingJson != null) s.OnboardingJson = req.OnboardingJson;
        if (req.HomeBannersJson != null) s.HomeBannersJson = req.HomeBannersJson;
        if (req.WorkHours != null) s.WorkHours = req.WorkHours;
        if (req.StoreLat.HasValue) s.StoreLat = req.StoreLat.Value;
        if (req.StoreLng.HasValue) s.StoreLng = req.StoreLng.Value;
        if (req.MinOrderAmount.HasValue) s.MinOrderAmount = req.MinOrderAmount.Value;
        if (req.DeliveryFeeType.HasValue) s.DeliveryFeeType = req.DeliveryFeeType.Value;
        if (req.DeliveryFeeValue.HasValue) s.DeliveryFeeValue = req.DeliveryFeeValue.Value;
        if (req.DeliveryFeePerKm.HasValue) s.DeliveryFeePerKm = Math.Max(0, req.DeliveryFeePerKm.Value);
        if (req.SupportPhone != null) s.SupportPhone = req.SupportPhone;
        if (req.SupportWhatsApp != null) s.SupportWhatsApp = req.SupportWhatsApp;
        if (req.FacebookUrl != null) s.FacebookUrl = req.FacebookUrl;
        if (req.InstagramUrl != null) s.InstagramUrl = req.InstagramUrl;
        if (req.TelegramUrl != null) s.TelegramUrl = req.TelegramUrl;

        if (req.IsManuallyClosed.HasValue)
        {
            s.IsManuallyClosed = req.IsManuallyClosed.Value;

            if (!req.IsAcceptingOrders.HasValue)
            {
                s.IsAcceptingOrders = !req.IsManuallyClosed.Value;
            }
        }
        if (req.IsAcceptingOrders.HasValue) s.IsAcceptingOrders = req.IsAcceptingOrders.Value;
        if (req.RoutingProfile != null)
        {
            s.RoutingProfile = string.IsNullOrWhiteSpace(req.RoutingProfile) ? "driving" : req.RoutingProfile.Trim();
        }

        if (req.DriverSpeedBikeKmH.HasValue) s.DriverSpeedBikeKmH = Math.Clamp(req.DriverSpeedBikeKmH.Value, 1m, 120m);
        if (req.DriverSpeedCarKmH.HasValue) s.DriverSpeedCarKmH = Math.Clamp(req.DriverSpeedCarKmH.Value, 1m, 160m);
        if (req.AiAutoReplyEnabled.HasValue) s.AiAutoReplyEnabled = req.AiAutoReplyEnabled.Value;
        if (req.AiAutoReplySystemPrompt != null) s.AiAutoReplySystemPrompt = string.IsNullOrWhiteSpace(req.AiAutoReplySystemPrompt) ? null : req.AiAutoReplySystemPrompt.Trim();

        s.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var payload = new
        {
            s.StoreName,
            s.PrimaryColorHex,
            s.SecondaryColorHex,
            s.OffersColorHex,
            s.WelcomeText,
            s.OnboardingJson,
            s.CustomerSplashUrl,
            s.DriverSplashUrl,
            s.SplashBackground1Url,
            s.SplashBackground2Url,
            s.LogoUrl,
            s.ClosedScreenImageUrl,
            s.SupportPhone,
            s.SupportWhatsApp,
            s.FacebookUrl,
            s.InstagramUrl,
            s.TelegramUrl,
            s.StoreLat,
            s.StoreLng,
            s.RoutingProfile,
            s.DriverSpeedBikeKmH,
            s.DriverSpeedCarKmH,
            updatedAtUtc = s.UpdatedAtUtc
        };
        await _notifyHub.Clients.All.SendAsync("settings_updated", payload);
        return Ok(payload);
    }

    [HttpGet("driver-tracks/{driverId:int}")]
    public async Task<IActionResult> DriverTracks(int driverId, [FromQuery] int minutes = 2)
    {
        minutes = Math.Clamp(minutes, 1, 10);
        var since = DateTime.UtcNow.AddMinutes(-minutes);
        var pts = await _db.DriverTrackPoints.AsNoTracking()
            .Where(p => p.DriverId == driverId && p.CreatedAtUtc >= since)
            .OrderBy(p => p.CreatedAtUtc)
            .Select(p => new { p.Lat, p.Lng, p.SpeedMps, p.HeadingDeg, p.CreatedAtUtc })
            .ToListAsync();
        return Ok(new { driverId, minutes, points = pts });
    }

    [HttpGet("order-tracks/{orderId:int}")]
    public async Task<IActionResult> OrderTracks(int orderId)
    {
        var o = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == orderId);
        if (o == null) return NotFound(new { error = "not_found" });

        var pts = await _db.DriverTrackPoints.AsNoTracking()
            .Where(p => p.OrderId == orderId)
            .OrderBy(p => p.CreatedAtUtc)
            .Select(p => new { p.Lat, p.Lng, p.SpeedMps, p.HeadingDeg, p.CreatedAtUtc })
            .ToListAsync();

        return Ok(new { orderId, distanceKm = Math.Round(o.DistanceKm, 3), points = pts });
    }

    [HttpGet("live-map")]
    public async Task<IActionResult> LiveMapData()
    {
        var s = await _db.StoreSettings.AsNoTracking().FirstAsync();

        var activeRaw = await _db.Orders.AsNoTracking()
            .Where(o => o.CurrentStatus != OrderStatus.Delivered && o.CurrentStatus != OrderStatus.Cancelled)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Select(o => new
            {
                o.Id,
                o.DriverId,
                o.CustomerId,
                o.CurrentStatus,
                o.DeliveryLat,
                o.DeliveryLng,
                o.DeliveryAddress,
                o.Total,
                o.DistanceKm,
                o.CreatedAtUtc
            })
            .ToListAsync();

        var custIds = activeRaw.Select(x => x.CustomerId).Distinct().ToList();
        var custMap = await _db.Customers.AsNoTracking()
            .Where(c => custIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.Phone })
            .ToDictionaryAsync(c => c.Id, c => new { c.Name, c.Phone });

        var active = activeRaw.Select(o => new
        {
            o.Id,
            o.DriverId,
            o.CustomerId,
            customerName = custMap.TryGetValue(o.CustomerId, out var cust1) ? (cust1.Name ?? string.Empty) : string.Empty,
            customerPhone = custMap.TryGetValue(o.CustomerId, out var cust2) ? cust2.Phone : null,
            o.CurrentStatus,
            o.DeliveryLat,
            o.DeliveryLng,
            o.DeliveryAddress,
            o.Total,
            o.DistanceKm,
            o.CreatedAtUtc
        }).ToList();

        var driverIds = active.Where(x => x.DriverId != null).Select(x => x.DriverId!.Value).Distinct().ToList();
        var locs = await _db.DriverLocations.AsNoTracking().Where(l => driverIds.Contains(l.DriverId)).ToListAsync();
        var driverNames = await _db.Drivers.AsNoTracking()
            .Where(d => driverIds.Contains(d.Id))
            .Select(d => new { d.Id, d.Name })
            .ToDictionaryAsync(d => d.Id, d => d.Name ?? $"سائق #{d.Id}");

        var driverTargets = active
            .Where(x => x.DriverId != null)
            .GroupBy(x => x.DriverId!.Value)
            .Select(g =>
            {
                var chosen = g.OrderByDescending(x => x.CurrentStatus == OrderStatus.WithDriver)
                              .ThenByDescending(x => x.CreatedAtUtc)
                              .First();
                var toStore = chosen.CurrentStatus != OrderStatus.WithDriver;
                var tLat = toStore ? s.StoreLat : chosen.DeliveryLat;
                var tLng = toStore ? s.StoreLng : chosen.DeliveryLng;

                if (toStore && (tLat == 0 || tLng == 0) && (chosen.DeliveryLat != 0 && chosen.DeliveryLng != 0))
                {
                    toStore = false;
                    tLat = chosen.DeliveryLat;
                    tLng = chosen.DeliveryLng;
                }

                var label = toStore ? "المتجر (استلام)" : $"الزبون (تسليم) – طلب #{chosen.Id}";
                return new { driverId = g.Key, orderId = chosen.Id, targetLat = tLat, targetLng = tLng, targetLabel = label, toStore };
            })
            .ToList();

        return Ok(new
        {
            store = new { lat = s.StoreLat, lng = s.StoreLng, name = s.StoreName, logoUrl = s.LogoUrl, routingProfile = s.RoutingProfile },
            orders = active,
            driverTargets,
            driverLocations = locs.Select(l => new { l.DriverId, driverName = driverNames.TryGetValue(l.DriverId, out var dn) ? dn : $"سائق #{l.DriverId}", l.Lat, l.Lng, l.SpeedMps, l.HeadingDeg, l.AccuracyMeters, l.UpdatedAtUtc })
        });
    }

    [HttpGet("order/{id:int}/warehouse-print")]
    public async Task<IActionResult> WarehousePrint(int id)
    {
        var o = await _db.Orders.AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Customer)
            .Include(x => x.StatusHistory)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (o == null) return NotFound();

        string esc(string s) => System.Net.WebUtility.HtmlEncode(s ?? "");

        var itemsHtml = "";
        foreach (var it in o.Items.OrderBy(x => x.Id))
        {
            var opts = (it.OptionsSnapshot ?? "").Trim();
            var modsLine = "";
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(opts);
                var parts = new List<string>();

                if (doc.RootElement.TryGetProperty("variantName", out var vName) && vName.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var vn = (vName.GetString() ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(vn)) parts.Add($"• النوع: {esc(vn)}");
                }

                if (doc.RootElement.TryGetProperty("addons", out var addons) && addons.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var a in addons.EnumerateArray())
                    {
                        if (a.ValueKind != System.Text.Json.JsonValueKind.Object) continue;

                        if (a.TryGetProperty("name", out var nEl) && nEl.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            var n = (nEl.GetString() ?? "").Trim();
                            if (!string.IsNullOrWhiteSpace(n)) parts.Add("• " + esc(n));
                        }
                        else if (a.TryGetProperty("Name", out var nEl2) && nEl2.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            var n = (nEl2.GetString() ?? "").Trim();
                            if (!string.IsNullOrWhiteSpace(n)) parts.Add("• " + esc(n));
                        }
                    }
                }

                if (doc.RootElement.TryGetProperty("note", out var noteEl) && noteEl.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var note = (noteEl.GetString() ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(note)) parts.Add($"• ملاحظة: {esc(note)}");
                }

                if (parts.Count > 0)
                    modsLine = "<div class='mods'>" + string.Join("<br/>", parts) + "</div>";
            }
            catch { }

            itemsHtml += $@"<div class='item'>
  <div class='row'>
    <div class='name'>{esc(it.ProductNameSnapshot)}</div>
    <div class='qty'>× {it.Quantity}</div>
  </div>
  {modsLine}
</div>";
        }

        var phone = o.Customer?.Phone ?? "";
        var created = o.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        var html = $@"<!doctype html>
<html lang='ar' dir='rtl'>
<head>
<meta charset='utf-8'/>
<meta name='viewport' content='width=device-width, initial-scale=1'/>
<title>طباعة المستودع - طلب #{o.Id}</title>
<style>
  body{{ font-family: Arial, sans-serif; margin:0; padding:18px; direction:rtl; text-align:right; }}
  .ticket{{ border:2px solid #000; border-radius:12px; padding:14px; direction:rtl; text-align:right; }}
  .orderNo{{ font-size:38px; font-weight:900; text-align:center; margin:0 0 8px; }}
  .meta{{ display:flex; justify-content:space-between; gap:12px; font-size:14px; margin-bottom:10px; }}
  .items{{ border-top:1px dashed #000; padding-top:10px; }}
  .item{{ padding:8px 0; border-bottom:1px dashed #ccc; }}
  .row{{ display:flex; justify-content:space-between; gap:10px; align-items:flex-start; }}
  .name{{ font-size:18px; font-weight:800; }}
  .qty{{ font-size:18px; font-weight:900; white-space:nowrap; }}
  .mods{{ margin-top:6px; font-size:14px; }}
  .footer{{ margin-top:10px; font-size:12px; text-align:center; color:#333; }}
  @media print {{
    body{{ padding:0; }}
    .noPrint{{ display:none; }}
    .ticket{{ border:none; border-radius:0; }}
  }}
</style>
</head>
<body>
  <div class='noPrint' style='margin-bottom:10px; display:flex; gap:8px;'>
    <button onclick='window.print()' style='padding:10px 14px; font-size:16px;'>🖨️ طباعة</button>
    <button onclick='window.close()' style='padding:10px 14px; font-size:16px;'>إغلاق</button>
  </div>
  <div class='ticket'>
    <div class='orderNo'>طلب رقم #{o.Id}</div>
    <div class='meta'>
      <div>الوقت: <b>{esc(created)}</b></div>
      <div>هاتف الزبون: <b>{esc(phone)}</b></div>
    </div>
    <div class='items'>
      {itemsHtml}
    </div>
    <div class='footer'>طباعة المستودع</div>
  </div>
<script>setTimeout(()=>{{/* keep */}}, 50);</script>
</body>
</html>";

        return Content(html, "text/html; charset=utf-8");
    }

    private static string AmountToWordsSyrian(decimal amount)
    {
        var whole = (int)decimal.Truncate(amount);
        if (whole <= 0) return "فقط صفر ليرة سورية";
        if (whole == 1) return "فقط ليرة واحدة سورية";
        if (whole == 2) return "فقط ليرتان سوريتان";
        return "فقط " + IntToArabicWords(whole) + " ليرة سورية";
    }

    private static readonly string[] OnesM = { "", "واحد", "اثنان", "ثلاثة", "أربعة", "خمسة", "ستة", "سبعة", "ثمانية", "تسعة" };
    private static readonly string[] Tens = { "", "عشر", "عشرون", "ثلاثون", "أربعون", "خمسون", "ستون", "سبعون", "ثمانون", "تسعون" };
    private static readonly string[] TensFrom10 = { "عشر", "إحدى عشرة", "اثنتا عشرة", "ثلاث عشرة", "أربع عشرة", "خمس عشرة", "ست عشرة", "سبع عشرة", "ثمان عشرة", "تسع عشرة" };
    private static readonly string[] Hundreds = { "", "مئة", "مئتان", "ثلاثمئة", "أربعمئة", "خمسمئة", "ستمئة", "سبعمئة", "ثمانمئة", "تسعمئة" };

    private static string IntToArabicWords(int n)
    {
        if (n == 0) return "صفر";
        if (n < 0) return "سالب " + IntToArabicWords(-n);
        if (n >= 1000)
        {
            var thousands = n / 1000;
            var rest = n % 1000;
            var t = thousands == 1 ? "ألف" : (thousands == 2 ? "ألفان" : IntToArabicWords(thousands) + " آلاف");
            if (thousands > 10) t = IntToArabicWords(thousands) + " ألف";
            return rest == 0 ? t : t + " و" + IntToArabicWords(rest);
        }
        if (n >= 100)
        {
            var h = n / 100;
            var rest = n % 100;
            var hr = Hundreds[h];
            if (h == 1 && rest > 0) hr = "مئة";
            return rest == 0 ? hr : hr + " و" + IntToArabicWords(rest);
        }
        if (n >= 20)
        {
            var ten = n / 10;
            var one = n % 10;
            return one == 0 ? Tens[ten] : OnesM[one] + " و" + Tens[ten];
        }
        if (n >= 10) return TensFrom10[n - 10];
        return OnesM[n];
    }

    public record AdminLoginReq(string Email, string Password);

    [HttpPost("auth/login")]
    [AllowAnonymous]
    public async Task<IActionResult> AppLogin(AdminLoginReq req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { error = "email_and_password_required" });

        var user = await _db.AdminUsers.AsNoTracking().FirstOrDefaultAsync(x => x.Email == req.Email);
        if (user == null || !AdminPassword.Verify(req.Password, user.PasswordHash, user.PasswordSalt))
            return Unauthorized(new { error = "invalid_credentials", message = "بريد إلكتروني أو كلمة مرور غير صحيحة" });

        var key = _opts.Value.AdminApiKey;
        return Ok(new { token = key, email = user.Email });
    }

    [HttpGet("order/{id:int}/receipt-print")]
    public async Task<IActionResult> ReceiptPrint(int id, [FromQuery] string? paper = null, [FromQuery] int? autoprint = null, [FromQuery] string? target = null)
    {
        var o = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (o == null) return NotFound();

        var msg = "الطباعة تتم من تطبيق الويندوز فقط. استخدم زر «طباعة» في لوحة الطلبات داخل تطبيق متجرنا للويندوز.";
        var html = $@"<!doctype html>
<html lang='ar' dir='rtl'>
<head><meta charset='utf-8'/></head>
<body style='font-family:Arial;padding:24px;text-align:center;max-width:400px;margin:40px auto;'>
  <p style='font-size:16px;'>{System.Net.WebUtility.HtmlEncode(msg)}</p>
  <p><small>طلب #{o.Id}</small></p>
  <button onclick='window.close()' style='padding:10px 16px;'>إغلاق</button>
</body>
</html>";
        return Content(html, "text/html; charset=utf-8");
    }

}

// Typed record to avoid dynamic binding exceptions
public record AgentCommissionRow(
    int agentId,
    string agentName,
    int orderCount,
    decimal totalSales,
    decimal totalCommission,
    decimal netToAgent
);
