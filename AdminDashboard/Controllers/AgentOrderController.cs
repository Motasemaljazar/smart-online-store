using AdminDashboard.Data;
using AdminDashboard.Entities;
using AdminDashboard.Hubs;
using AdminDashboard.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AdminDashboard.Controllers;

[ApiController]
[Route("api/agent")]
public class AgentOrderController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<AgentOrderController> _logger;
    private readonly IHubContext<NotifyHub> _hub;
    private readonly NotificationService _notifications;

    public AgentOrderController(AppDbContext db, ILogger<AgentOrderController> logger, IHubContext<NotifyHub> hub, NotificationService notifications)
    {
        _db = db; _logger = logger; _hub = hub; _notifications = notifications;
    }

    // ── Pending Orders ────────────────────────────────────────────────────────
    [HttpGet("orders/pending")]
    [AgentAuth]
    public async Task<IActionResult> GetPendingOrders()
    {
        var agentId = GetAgentId();
        var now = DateTime.UtcNow;
        // فقط الطلبات التي لم يتم تأكيدها أو تعيينها بعد من لوحة التحكم
        var items = await _db.OrderAgentItems
            .Include(x => x.Order).ThenInclude(o => o!.Items)
            .Where(x => x.AgentId == agentId
                     && x.AgentStatus == AgentOrderStatus.Pending
                     && x.Order!.CurrentStatus == OrderStatus.New)
            .OrderBy(x => x.AutoAcceptAt)
            .Select(x => new
            {
                x.Id,
                x.OrderId,
                x.AgentSubtotal,
                x.AutoAcceptAt,
                SecondsRemaining = (int)(x.AutoAcceptAt - now).TotalSeconds,
                CreatedAt = x.CreatedAtUtc,
                Items = x.Order!.Items.Select(i => new
                {
                    i.ProductId,
                    i.ProductNameSnapshot,
                    i.UnitPriceSnapshot,
                    i.Quantity
                })
            })
            .ToListAsync();
        return Ok(items);
    }

    // ── Accept Order ──────────────────────────────────────────────────────────
    [HttpPost("orders/{orderId:int}/accept")]
    [AgentAuth]
    public async Task<IActionResult> AcceptOrder(int orderId)
    {
        var agentId = GetAgentId();
        var item = await _db.OrderAgentItems
            .Include(x => x.Order).ThenInclude(o => o!.Items)
            .FirstOrDefaultAsync(x => x.OrderId == orderId && x.AgentId == agentId);

        if (item == null) return NotFound(new { error = "الطلب غير موجود" });
        if (item.AgentStatus != AgentOrderStatus.Pending)
            return BadRequest(new { error = "الطلب تمت معالجته مسبقاً" });

        item.AgentStatus = AgentOrderStatus.Accepted;
        item.RespondedAtUtc = DateTime.UtcNow;

        // ✅ تسجيل العمولة فوراً عند قبول الطلب
        var existingCommission = await _db.AgentCommissions
            .FirstOrDefaultAsync(c => c.OrderId == orderId && c.AgentId == agentId);

        if (existingCommission == null && item.Order != null)
        {
            var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Id == agentId);
            var commissionPercent = item.CommissionPercent > 0
                ? item.CommissionPercent
                : (agent?.CommissionPercent ?? 0m);

            var saleAmount = item.AgentSubtotal > 0
                ? item.AgentSubtotal
                : item.Order.Total;

            var commissionAmount = Math.Round(saleAmount * commissionPercent / 100m, 2);

            _db.AgentCommissions.Add(new AgentCommission
            {
                AgentId = agentId,
                OrderId = orderId,
                SaleAmount = saleAmount,
                CommissionPercent = commissionPercent,
                CommissionAmount = commissionAmount,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        if (item.Order != null && (item.Order.CurrentStatus == OrderStatus.New || item.Order.CurrentStatus == OrderStatus.Confirmed))
        {
            item.Order.CurrentStatus = OrderStatus.Accepted;
            _db.OrderStatusHistory.Add(new OrderStatusHistory
            {
                OrderId = orderId,
                Status = OrderStatus.Accepted,
                Comment = "تم قبول الطلب من قبل المندوب",
                ChangedByType = "agent",
                ChangedById = agentId,
                ChangedAtUtc = DateTime.UtcNow
            });
        }
        await _db.SaveChangesAsync();

        if (item.Order != null)
        {
            await _notifications.CreateAndBroadcastAsync(NotificationUserType.Customer, item.Order.CustomerId,
                "تم قبول طلبك ✅", $"تم قبول طلبك رقم #{orderId} وسيتم تجهيزه قريباً.", orderId);
            await _hub.Clients.Group($"customer-{item.Order.CustomerId}")
                .SendAsync("order_status", new { orderId, status = item.Order.CurrentStatus, agentAccepted = true });
            await _notifications.CreateAndBroadcastAsync(NotificationUserType.Admin, null,
                "قبول مندوب للطلب", $"المندوب قبل الطلب #{orderId}", orderId);
            await _hub.Clients.Group("admin").SendAsync("order_status", new { orderId, status = item.Order.CurrentStatus });
        }
        return Ok(new { success = true });
    }

    // ── Reject Order ──────────────────────────────────────────────────────────
    [HttpPost("orders/{orderId:int}/reject")]
    [AgentAuth]
    public async Task<IActionResult> RejectOrder(int orderId, [FromBody] RejectOrderRequest req)
    {
        var agentId = GetAgentId();
        var item = await _db.OrderAgentItems.FirstOrDefaultAsync(x => x.OrderId == orderId && x.AgentId == agentId);
        if (item == null) return NotFound(new { error = "الطلب غير موجود" });
        if (item.AgentStatus != AgentOrderStatus.Pending)
            return BadRequest(new { error = "الطلب تمت معالجته مسبقاً" });

        item.AgentStatus = AgentOrderStatus.Rejected;
        item.RejectionReason = req.Reason;
        item.RespondedAtUtc = DateTime.UtcNow;
        _db.Notifications.Add(new Notification
        {
            UserType = NotificationUserType.Admin,
            UserId = null,
            Title = "رفض مندوب طلباً",
            Body = $"المندوب رفض الطلب #{orderId}. السبب: {req.Reason}",
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // ── Active Orders ─────────────────────────────────────────────────────────
    [HttpGet("orders/active")]
    [AgentAuth]
    public async Task<IActionResult> GetActiveOrders()
    {
        var agentId = GetAgentId();
        var items = await _db.OrderAgentItems
            .Include(x => x.Order)
            .Where(x => x.AgentId == agentId &&
                        (x.AgentStatus == AgentOrderStatus.Accepted ||
                         x.AgentStatus == AgentOrderStatus.AutoAccepted ||
                         // طلبات Pending تم تأكيدها أو تعيينها من لوحة التحكم
                         (x.AgentStatus == AgentOrderStatus.Pending && x.Order!.CurrentStatus > OrderStatus.New)) &&
                        x.Order!.CurrentStatus < OrderStatus.Delivered &&
                        x.Order!.CurrentStatus != OrderStatus.Cancelled)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.OrderId,
                x.AgentSubtotal,
                AgentStatusName = x.AgentStatus.ToString(),
                OrderStatus = x.Order!.CurrentStatus.ToString(),
                OrderStatusInt = (int)x.Order!.CurrentStatus,
                CreatedAt = x.Order!.CreatedAtUtc
            })
            .ToListAsync();
        return Ok(items);
    }

    // ── Order History ─────────────────────────────────────────────────────────
    [HttpGet("orders/history")]
    [AgentAuth]
    public async Task<IActionResult> GetOrderHistory([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var agentId = GetAgentId();
        var items = await _db.OrderAgentItems
            .Include(x => x.Order)
            .Where(x => x.AgentId == agentId && x.Order!.CurrentStatus >= OrderStatus.Delivered)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.OrderId,
                x.AgentSubtotal,
                AgentStatus = x.AgentStatus.ToString(),
                OrderStatus = x.Order!.CurrentStatus.ToString(),
                OrderStatusInt = (int)x.Order!.CurrentStatus,
                DeliveredAt = x.Order!.DeliveredAtUtc
            })
            .ToListAsync();
        return Ok(items);
    }

    // ── Daily Report ──────────────────────────────────────────────────────────
    [HttpGet("reports/daily")]
    [AgentAuth]
    public async Task<IActionResult> GetDailyReport()
    {
        var agentId = GetAgentId();
        var localNow = DateTime.Now;
        var localToday = new DateTime(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, DateTimeKind.Local);
        var today = localToday.ToUniversalTime();
        var tomorrow = localToday.AddDays(1).ToUniversalTime();

        // جلب العمولات المسجلة أولاً
        var commissions = await _db.AgentCommissions
            .Where(c => c.AgentId == agentId && c.CreatedAtUtc >= today && c.CreatedAtUtc < tomorrow)
            .ToListAsync();

        // إذا لا توجد عمولات - نحسب من الطلبات المُسلَّمة مباشرة
        if (commissions.Count == 0)
        {
            var agent = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId);
            var commissionPct = agent?.CommissionPercent ?? 0m;

            var agentItems = await _db.OrderAgentItems.AsNoTracking()
                .Include(ai => ai.Order)
                .Where(ai => ai.AgentId == agentId
                    && ai.Order != null
                    && ai.Order.CurrentStatus == OrderStatus.Delivered
                    && (ai.Order.DeliveredAtUtc.HasValue
                        ? (ai.Order.DeliveredAtUtc >= today && ai.Order.DeliveredAtUtc < tomorrow)
                        : (ai.Order.CreatedAtUtc >= today && ai.Order.CreatedAtUtc < tomorrow)))
                .ToListAsync();

            var totalSales = agentItems.Sum(ai => ai.AgentSubtotal > 0 ? ai.AgentSubtotal : (ai.Order?.Total ?? 0));
            var commissionDue = agentItems.Sum(ai =>
            {
                var pct = ai.CommissionPercent > 0 ? ai.CommissionPercent : commissionPct;
                var sale = ai.AgentSubtotal > 0 ? ai.AgentSubtotal : (ai.Order?.Total ?? 0);
                return Math.Round(sale * pct / 100m, 2);
            });

            return Ok(new
            {
                Date = today,
                OrderCount = agentItems.Count,
                TotalSales = totalSales,
                CommissionDue = commissionDue,
                NetIncome = totalSales - commissionDue
            });
        }

        return Ok(new
        {
            Date = today,
            OrderCount = commissions.Count,
            TotalSales = commissions.Sum(c => c.SaleAmount),
            CommissionDue = commissions.Sum(c => c.CommissionAmount),
            NetIncome = commissions.Sum(c => c.SaleAmount - c.CommissionAmount)
        });
    }

    // ── Monthly Report ────────────────────────────────────────────────────────
    [HttpGet("reports/monthly")]
    [AgentAuth]
    public async Task<IActionResult> GetMonthlyReport([FromQuery] int? year, [FromQuery] int? month)
    {
        var agentId = GetAgentId();
        var now = DateTime.UtcNow;
        var y = year ?? now.Year;
        var m = month ?? now.Month;
        var start = new DateTime(y, m, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);

        var commissions = await _db.AgentCommissions
            .Where(c => c.AgentId == agentId && c.CreatedAtUtc >= start && c.CreatedAtUtc < end)
            .OrderByDescending(c => c.CreatedAtUtc)
            .Select(c => new
            {
                c.OrderId,
                c.SaleAmount,
                c.CommissionPercent,
                c.CommissionAmount,
                NetIncome = c.SaleAmount - c.CommissionAmount,
                c.SettledAt,
                Date = c.CreatedAtUtc
            })
            .ToListAsync();

        // إذا لا توجد عمولات - نحسب من الطلبات المُسلَّمة مباشرة
        if (commissions.Count == 0)
        {
            var agent = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId);
            var commissionPct = agent?.CommissionPercent ?? 0m;

            var agentItems = await _db.OrderAgentItems.AsNoTracking()
                .Include(ai => ai.Order)
                .Where(ai => ai.AgentId == agentId
                    && ai.Order != null
                    && ai.Order.CurrentStatus == OrderStatus.Delivered
                    && (ai.Order.DeliveredAtUtc.HasValue
                        ? (ai.Order.DeliveredAtUtc >= start && ai.Order.DeliveredAtUtc < end)
                        : (ai.Order.CreatedAtUtc >= start && ai.Order.CreatedAtUtc < end)))
                .OrderByDescending(ai => ai.Order!.DeliveredAtUtc ?? ai.Order!.CreatedAtUtc)
                .ToListAsync();

            var details = agentItems.Select(ai =>
            {
                var pct = ai.CommissionPercent > 0 ? ai.CommissionPercent : commissionPct;
                var sale = ai.AgentSubtotal > 0 ? ai.AgentSubtotal : (ai.Order?.Total ?? 0);
                var comm = Math.Round(sale * pct / 100m, 2);
                return new
                {
                    OrderId = ai.OrderId,
                    SaleAmount = sale,
                    CommissionPercent = pct,
                    CommissionAmount = comm,
                    NetIncome = sale - comm,
                    SettledAt = (DateTime?)null,
                    Date = ai.Order?.DeliveredAtUtc ?? ai.Order?.CreatedAtUtc ?? DateTime.UtcNow
                };
            }).ToList();

            return Ok(new
            {
                Year = y,
                Month = m,
                TotalOrders = details.Count,
                TotalSales = details.Sum(d => d.SaleAmount),
                TotalCommission = details.Sum(d => d.CommissionAmount),
                TotalNetIncome = details.Sum(d => d.NetIncome),
                Details = details
            });
        }

        return Ok(new
        {
            Year = y,
            Month = m,
            TotalOrders = commissions.Count,
            TotalSales = commissions.Sum(c => c.SaleAmount),
            TotalCommission = commissions.Sum(c => c.CommissionAmount),
            TotalNetIncome = commissions.Sum(c => c.SaleAmount - c.CommissionAmount),
            Details = commissions
        });
    }

    // ── Top Products ──────────────────────────────────────────────────────────
    [HttpGet("reports/top-products")]
    [AgentAuth]
    public async Task<IActionResult> GetTopProducts([FromQuery] int limit = 10)
    {
        var agentId = GetAgentId();

        // جلب البيانات أولاً ثم الحساب في الـ memory (SQLite لا يدعم Sum على decimal)
        var rawItems = await _db.OrderItems
            .Include(oi => oi.Order)
            .Where(oi => _db.Products.Any(p => p.Id == oi.ProductId && p.AgentId == agentId)
                      && oi.Order!.CurrentStatus != OrderStatus.Cancelled)
            .Select(oi => new {
                oi.ProductId,
                oi.ProductNameSnapshot,
                oi.Quantity,
                UnitPrice = (double)oi.UnitPriceSnapshot
            })
            .ToListAsync();

        var topProducts = rawItems
            .GroupBy(oi => new { oi.ProductId, oi.ProductNameSnapshot })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.ProductNameSnapshot,
                TotalQuantity = g.Sum(oi => oi.Quantity),
                TotalRevenue = (decimal)g.Sum(oi => oi.UnitPrice * oi.Quantity)
            })
            .OrderByDescending(x => x.TotalRevenue)
            .Take(limit)
            .ToList();

        return Ok(topProducts);
    }

    // ── Chats ─────────────────────────────────────────────────────────────────
    [HttpGet("chats")]
    [AgentAuth]
    public async Task<IActionResult> GetChats()
    {
        var agentId = GetAgentId();
        var threads = await _db.ProductAgentChats
            .Include(t => t.Customer).Include(t => t.Product).Include(t => t.Messages)
            .Where(t => t.AgentId == agentId)
            .OrderByDescending(t => t.Messages.Max(m => (DateTime?)m.CreatedAtUtc) ?? t.CreatedAtUtc)
            .Select(t => new
            {
                t.Id,
                CustomerName = t.Customer!.Name,
                ProductName = t.Product != null ? t.Product.Name : (string?)null,
                LastMessage = t.Messages.OrderByDescending(m => m.CreatedAtUtc).Select(m => m.Message).FirstOrDefault(),
                LastMessageAt = t.Messages.Max(m => (DateTime?)m.CreatedAtUtc)
            })
            .ToListAsync();
        return Ok(threads);
    }

    [HttpGet("chats/{threadId:int}")]
    [AgentAuth]
    public async Task<IActionResult> GetChatThread(int threadId)
    {
        var agentId = GetAgentId();
        var thread = await _db.ProductAgentChats
            .Include(t => t.Customer).Include(t => t.Product)
            .Include(t => t.Messages.OrderBy(m => m.CreatedAtUtc))
            .FirstOrDefaultAsync(t => t.Id == threadId && t.AgentId == agentId);
        if (thread == null) return NotFound();
        return Ok(new
        {
            id = thread.Id,
            customerId = thread.CustomerId,
            customerName = thread.Customer?.Name ?? "",
            productName = thread.Product?.Name,
            messages = thread.Messages.Select(m => new
            { id = m.Id, fromAgent = m.FromAgent, message = m.Message, createdAtUtc = m.CreatedAtUtc })
        });
    }

    [HttpPost("chats/{threadId:int}/messages")]
    [AgentAuth]
    public async Task<IActionResult> SendMessage(int threadId, [FromBody] SendMessageRequest req)
    {
        var agentId = GetAgentId();
        var thread = await _db.ProductAgentChats.FirstOrDefaultAsync(t => t.Id == threadId && t.AgentId == agentId);
        if (thread == null) return NotFound();
        var message = new ProductAgentChatMessage
        { ThreadId = threadId, FromAgent = true, Message = req.Message, CreatedAtUtc = DateTime.UtcNow };
        _db.ProductAgentChatMessages.Add(message);
        await _db.SaveChangesAsync();
        await _hub.Clients.Group($"customer-{thread.CustomerId}").SendAsync("new_chat_message",
            new { threadId, id = message.Id, fromAgent = true, message = message.Message, createdAtUtc = message.CreatedAtUtc });
        try
        {
            var snippet = req.Message.Length > 60 ? req.Message[..60] + "…" : req.Message;
            await _notifications.CreateAndBroadcastAsync(NotificationUserType.Customer, thread.CustomerId,
                "رسالة جديدة من المندوب", snippet, null);
        }
        catch { }
        return Ok(new { message.Id, message.CreatedAtUtc });
    }

    // ── Order Detail ──────────────────────────────────────────────────────────
    [HttpGet("orders/{orderId:int}")]
    [AgentAuth]
    public async Task<IActionResult> GetOrderDetails(int orderId)
    {
        var agentId = GetAgentId();
        var item = await _db.OrderAgentItems
            .Include(x => x.Order).ThenInclude(o => o!.Items)
            .Include(x => x.Order).ThenInclude(o => o!.Customer)
            .Include(x => x.Order).ThenInclude(o => o!.Driver)
            .FirstOrDefaultAsync(x => x.OrderId == orderId && x.AgentId == agentId);
        if (item == null) return NotFound(new { error = "الطلب غير موجود" });
        var o = item.Order!;
        var commissionAmount = Math.Round(item.AgentSubtotal * item.CommissionPercent / 100m, 2);
        return Ok(new
        {
            o.Id,
            o.CurrentStatus,
            CurrentStatusInt = (int)o.CurrentStatus,
            o.Total,
            o.DeliveryAddress,
            o.DeliveryLat,
            o.DeliveryLng,
            o.Notes,
            o.ProcessingEtaMinutes,
            o.DeliveryEtaMinutes,
            o.CreatedAtUtc,
            o.DeliveredAtUtc,
            AgentStatus = item.AgentStatus.ToString(),
            AgentStatusInt = (int)item.AgentStatus,
            item.AgentSubtotal,
            item.CommissionPercent,
            CommissionAmount = commissionAmount,
            NetIncome = item.AgentSubtotal - commissionAmount,
            Driver = o.Driver == null ? null : new { o.Driver.Id, o.Driver.Name, o.Driver.Phone, o.Driver.VehicleType },
            Customer = o.Customer == null ? null : new { o.Customer.Name, o.Customer.Phone },
            Items = o.Items.Select(i => new
            {
                i.ProductId,
                i.ProductNameSnapshot,
                i.UnitPriceSnapshot,
                i.Quantity,
                Total = i.UnitPriceSnapshot * i.Quantity
            })
        });
    }

    // ── Update Status ─────────────────────────────────────────────────────────
    // الإصلاح الجذري: يكفي أن الطلب مرتبط بالمندوب — بدون شرط Accepted
    [HttpPost("orders/{orderId:int}/status")]
    [AgentAuth]
    public async Task<IActionResult> UpdateOrderStatus(int orderId, AgentUpdateStatusReq req)
    {
        var agentId = GetAgentId();
        var item = await _db.OrderAgentItems
            .FirstOrDefaultAsync(x => x.OrderId == orderId && x.AgentId == agentId);
        if (item == null)
            return NotFound(new { error = "الطلب غير موجود أو لا ينتمي لهذا المندوب" });

        var o = await _db.Orders.FirstOrDefaultAsync(x => x.Id == orderId);
        if (o == null) return NotFound(new { error = "الطلب غير موجود في النظام" });
        if (o.CurrentStatus == OrderStatus.Delivered || o.CurrentStatus == OrderStatus.Cancelled)
            return BadRequest(new { error = "لا يمكن تغيير حالة طلب منتهٍ" });

        // قبول ضمني إذا كان الطلب لا يزال Pending
        if (item.AgentStatus == AgentOrderStatus.Pending)
        {
            item.AgentStatus = AgentOrderStatus.Accepted;
            item.RespondedAtUtc = DateTime.UtcNow;
        }

        o.CurrentStatus = req.Status;
        _db.OrderStatusHistory.Add(new OrderStatusHistory
        {
            OrderId = o.Id,
            Status = req.Status,
            Comment = req.Comment ?? "تحديث حالة من المندوب",
            ChangedByType = "agent",
            ChangedById = agentId,
            ChangedAtUtc = DateTime.UtcNow
        });

        // تسجيل العمولة عند التسليم
        if (req.Status == OrderStatus.Delivered)
        {
            o.DeliveredAtUtc = DateTime.UtcNow;
            var existingCommission = await _db.AgentCommissions
                .AnyAsync(c => c.OrderId == o.Id && c.AgentId == agentId);
            if (!existingCommission)
            {
                var agent = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId);
                // اعطي الاولوية للنسبة المحفوظة في الطلب، وإذا كانت صفراً خذها من بيانات المندوب
                var commissionPercent = item.CommissionPercent > 0
                    ? item.CommissionPercent
                    : (agent?.CommissionPercent ?? 0m);

                var saleAmount = item.AgentSubtotal > 0 ? item.AgentSubtotal : o.Total;
                var commissionAmount = Math.Round(saleAmount * commissionPercent / 100m, 2);

                // تحديث نسبة العمولة في سجل الطلب إذا كانت صفراً
                if (item.CommissionPercent == 0 && commissionPercent > 0)
                    item.CommissionPercent = commissionPercent;

                _db.AgentCommissions.Add(new AgentCommission
                {
                    AgentId = agentId,
                    OrderId = o.Id,
                    SaleAmount = saleAmount,
                    CommissionPercent = commissionPercent,
                    CommissionAmount = commissionAmount,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }
        }

        await _db.SaveChangesAsync();

        var statusArabic = req.Status switch
        {
            OrderStatus.Confirmed => "مؤكد",
            OrderStatus.Preparing => "قيد المعالجة",
            OrderStatus.ReadyForPickup => "جاهز للاستلام",
            OrderStatus.WithDriver => "مع السائق",
            OrderStatus.Delivered => "تم التسليم ✅",
            OrderStatus.Cancelled => "ملغي",
            OrderStatus.Accepted => "مقبول",
            _ => req.Status.ToString()
        };

        await _notifications.CreateAndBroadcastAsync(NotificationUserType.Customer, o.CustomerId,
            "تحديث حالة طلبك", $"طلبك #{orderId} أصبح: {statusArabic}", orderId);
        await _hub.Clients.Group("admin").SendAsync("order_status", new { orderId = o.Id, status = o.CurrentStatus });
        await _hub.Clients.Group($"customer-{o.CustomerId}").SendAsync("order_status", new { orderId = o.Id, status = o.CurrentStatus });
        if (o.DriverId.HasValue)
            await _hub.Clients.Group($"driver-{o.DriverId}").SendAsync("order_status", new { orderId = o.Id, status = o.CurrentStatus });

        return Ok(new { success = true, status = o.CurrentStatus, statusInt = (int)o.CurrentStatus, statusArabic });
    }

    // ── Assign Driver ─────────────────────────────────────────────────────────
    // الإصلاح: بدون شرط Accepted
    [HttpPost("orders/{orderId:int}/assign-driver")]
    [AgentAuth]
    public async Task<IActionResult> AssignDriver(int orderId, AgentAssignDriverReq req)
    {
        var agentId = GetAgentId();
        var item = await _db.OrderAgentItems
            .FirstOrDefaultAsync(x => x.OrderId == orderId && x.AgentId == agentId);
        if (item == null)
            return NotFound(new { error = "الطلب غير موجود أو لا ينتمي لهذا المندوب" });

        var o = await _db.Orders.FirstOrDefaultAsync(x => x.Id == orderId);
        if (o == null) return NotFound(new { error = "not_found" });
        if (o.CurrentStatus == OrderStatus.Delivered || o.CurrentStatus == OrderStatus.Cancelled)
            return BadRequest(new { error = "لا يمكن تعيين سائق لطلب منتهٍ" });

        var driver = await _db.Drivers.AsNoTracking().FirstOrDefaultAsync(d => d.Id == req.DriverId);
        if (driver == null) return BadRequest(new { error = "السائق غير موجود" });

        var activeCount = await _db.Orders.AsNoTracking().CountAsync(x =>
            x.DriverId == req.DriverId && x.Id != o.Id &&
            x.CurrentStatus != OrderStatus.Delivered && x.CurrentStatus != OrderStatus.Cancelled);
        if (activeCount >= 15)
            return BadRequest(new { error = "السائق وصل لحد الطلبات النشطة" });

        // قبول ضمني
        if (item.AgentStatus == AgentOrderStatus.Pending)
        {
            item.AgentStatus = AgentOrderStatus.Accepted;
            item.RespondedAtUtc = DateTime.UtcNow;
        }

        o.DriverId = req.DriverId;
        o.CurrentStatus = OrderStatus.ReadyForPickup;
        _db.OrderStatusHistory.Add(new OrderStatusHistory
        {
            OrderId = o.Id,
            Status = OrderStatus.ReadyForPickup,
            Comment = $"تم تعيين السائق: {driver.Name} من قبل المندوب",
            ChangedByType = "agent",
            ChangedById = agentId,
            ChangedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        await _hub.Clients.Group($"driver-{req.DriverId}").SendAsync("order_assigned", new { orderId = o.Id });
        await _notifications.CreateAndBroadcastAsync(NotificationUserType.Customer, o.CustomerId,
            "تم تعيين سائق لطلبك 🚗", $"تم تعيين السائق {driver.Name} لتوصيل طلبك رقم #{orderId}.", orderId);
        await _notifications.CreateAndBroadcastAsync(NotificationUserType.Admin, null,
            "تعيين سائق من مندوب", $"المندوب عيّن السائق {driver.Name} للطلب #{orderId}", orderId);
        await _hub.Clients.Group("admin").SendAsync("order_assigned", new { orderId = o.Id, driverId = o.DriverId });
        await _hub.Clients.Group($"customer-{o.CustomerId}").SendAsync("order_status",
            new { orderId = o.Id, status = o.CurrentStatus, driverId = o.DriverId });

        return Ok(new { success = true, driverName = driver.Name, orderId, status = o.CurrentStatus });
    }

    // ── Set ETA ───────────────────────────────────────────────────────────────
    // الإصلاح: بدون شرط Accepted
    [HttpPost("orders/{orderId:int}/set-eta")]
    [AgentAuth]
    public async Task<IActionResult> SetEta(int orderId, AgentSetEtaReq req)
    {
        var agentId = GetAgentId();
        var item = await _db.OrderAgentItems
            .FirstOrDefaultAsync(x => x.OrderId == orderId && x.AgentId == agentId);
        if (item == null)
            return NotFound(new { error = "الطلب غير موجود أو لا ينتمي لهذا المندوب" });

        var o = await _db.Orders.FirstOrDefaultAsync(x => x.Id == orderId);
        if (o == null) return NotFound();

        if (req.ProcessingEtaMinutes.HasValue) o.ProcessingEtaMinutes = req.ProcessingEtaMinutes.Value;
        if (req.DeliveryEtaMinutes.HasValue) o.DeliveryEtaMinutes = req.DeliveryEtaMinutes.Value;
        await _db.SaveChangesAsync();

        var etaMsg = "";
        if (req.ProcessingEtaMinutes.HasValue) etaMsg += $"وقت التحضير: {req.ProcessingEtaMinutes} دقيقة. ";
        if (req.DeliveryEtaMinutes.HasValue) etaMsg += $"وقت التوصيل: {req.DeliveryEtaMinutes} دقيقة.";
        if (!string.IsNullOrEmpty(etaMsg))
            await _notifications.CreateAndBroadcastAsync(NotificationUserType.Customer, o.CustomerId,
                "تحديث وقت الطلب ⏱️", $"طلبك #{orderId}: {etaMsg}", orderId);

        return Ok(new { success = true, o.ProcessingEtaMinutes, o.DeliveryEtaMinutes });
    }

    // ── Available Drivers ─────────────────────────────────────────────────────
    [HttpGet("drivers")]
    [AgentAuth]
    public async Task<IActionResult> GetAvailableDrivers()
    {
        var drivers = await _db.Drivers.AsNoTracking()
            .Where(d => d.Status == DriverStatus.Available)
            .Select(d => new { d.Id, d.Name, d.Phone, d.VehicleType, d.Status })
            .ToListAsync();
        return Ok(drivers);
    }

    private int GetAgentId()
    {
        var claim = User.FindFirst("agentId")?.Value ?? HttpContext.Items["agentId"]?.ToString();
        return int.TryParse(claim, out var id) ? id : 0;
    }
}

public record RejectOrderRequest(string Reason);
public record SendMessageRequest(string Message);

public record AgentAssignDriverReq(int DriverId);
public record AgentSetEtaReq(int? ProcessingEtaMinutes, int? DeliveryEtaMinutes);
public record AgentUpdateStatusReq(OrderStatus Status, string? Comment);
