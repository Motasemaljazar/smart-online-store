using AdminDashboard.Data;
using Microsoft.AspNetCore.Authorization;
using AdminDashboard.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdminDashboard.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "AdminOnly")]
public class AdminCommissionsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AdminCommissionsController(AppDbContext db) => _db = db;

    [HttpGet("commissions")]
    public async Task<IActionResult> GetAllCommissions([FromQuery] int? month, [FromQuery] int? year)
    {
        var query = _db.AgentCommissions
            .Include(c => c.Agent)
            .AsQueryable();

        if (month.HasValue && year.HasValue)
        {
            var start = new DateTime(year.Value, month.Value, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = start.AddMonths(1);
            query = query.Where(c => c.CreatedAtUtc >= start && c.CreatedAtUtc < end);
        }

        var grouped = await query
            .GroupBy(c => new { c.AgentId, c.Agent!.Name })
            .Select(g => new
            {
                g.Key.AgentId,
                AgentName = g.Key.Name,
                CommissionPercent = Math.Round(g.Average(c => c.CommissionPercent), 2),
                TotalSales = g.Sum(c => c.SaleAmount),
                TotalCommission = g.Sum(c => c.CommissionAmount),
                TotalSettled = g.Where(c => c.SettledAt != null).Sum(c => c.CommissionAmount),
                TotalUnsettled = g.Where(c => c.SettledAt == null).Sum(c => c.CommissionAmount),
                OrderCount = g.Count()
            })
            .ToListAsync();

        // fallback: إذا لا توجد عمولات مسجلة، احسب من OrderAgentItems (الطلبات المسلمة)
        if (grouped.Count == 0)
        {
            var agentItemQuery = _db.OrderAgentItems
                .Include(ai => ai.Agent)
                .Include(ai => ai.Order)
                .Where(ai => ai.Order!.CurrentStatus == AdminDashboard.Entities.OrderStatus.Delivered)
                .AsQueryable();

            if (month.HasValue && year.HasValue)
            {
                var start = new DateTime(year.Value, month.Value, 1, 0, 0, 0, DateTimeKind.Utc);
                var end = start.AddMonths(1);
                agentItemQuery = agentItemQuery.Where(ai =>
                    (ai.Order!.DeliveredAtUtc.HasValue ? ai.Order.DeliveredAtUtc >= start && ai.Order.DeliveredAtUtc < end
                                                       : ai.Order.CreatedAtUtc >= start && ai.Order.CreatedAtUtc < end));
            }

            var agentItems = await agentItemQuery.ToListAsync();

            if (agentItems.Any())
            {
                var fallback = agentItems
                    .GroupBy(ai => new { ai.AgentId, Name = ai.Agent?.Name ?? $"مندوب #{ai.AgentId}" })
                    .Select(g =>
                    {
                        var items = g.ToList();
                        var pct = items.Average(ai => ai.CommissionPercent > 0 ? (double)ai.CommissionPercent : (double)(ai.Agent?.CommissionPercent ?? 0m));
                        var sales = items.Sum(ai => ai.AgentSubtotal > 0 ? ai.AgentSubtotal : (ai.Order?.Total ?? 0m));
                        var comm = items.Sum(ai =>
                        {
                            var p = ai.CommissionPercent > 0 ? ai.CommissionPercent : (ai.Agent?.CommissionPercent ?? 0m);
                            var s = ai.AgentSubtotal > 0 ? ai.AgentSubtotal : (ai.Order?.Total ?? 0m);
                            return Math.Round(s * p / 100m, 2);
                        });
                        return new
                        {
                            AgentId = g.Key.AgentId,
                            AgentName = g.Key.Name,
                            CommissionPercent = Math.Round((decimal)pct, 2),
                            TotalSales = sales,
                            TotalCommission = comm,
                            TotalSettled = 0m,
                            TotalUnsettled = comm,
                            OrderCount = items.Count
                        };
                    })
                    .OrderByDescending(x => x.TotalSales)
                    .ToList();

                return Ok(fallback);
            }
        }

        return Ok(grouped);
    }

    [HttpGet("commissions/{agentId:int}")]
    public async Task<IActionResult> GetAgentCommissions(int agentId, [FromQuery] int? month, [FromQuery] int? year)
    {
        var query = _db.AgentCommissions
            .Include(c => c.Order)
            .Where(c => c.AgentId == agentId);

        if (month.HasValue && year.HasValue)
        {
            var start = new DateTime(year.Value, month.Value, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = start.AddMonths(1);
            query = query.Where(c => c.CreatedAtUtc >= start && c.CreatedAtUtc < end);
        }

        var commissions = await query
            .OrderByDescending(c => c.CreatedAtUtc)
            .Select(c => new
            {
                c.Id,
                c.OrderId,
                c.SaleAmount,
                c.CommissionPercent,
                c.CommissionAmount,
                NetToAgent = c.SaleAmount - c.CommissionAmount,
                c.SettledAt,
                Date = c.CreatedAtUtc
            })
            .ToListAsync();

        var agent = await _db.Agents.FindAsync(agentId);

        return Ok(new
        {
            Agent = new { agent?.Id, agent?.Name, agent?.CommissionPercent },
            TotalSales = commissions.Sum(c => c.SaleAmount),
            TotalCommission = commissions.Sum(c => c.CommissionAmount),
            TotalSettled = commissions.Where(c => c.SettledAt != null).Sum(c => c.CommissionAmount),
            TotalUnsettled = commissions.Where(c => c.SettledAt == null).Sum(c => c.CommissionAmount),
            Commissions = commissions
        });
    }

    [HttpPost("commissions/{agentId:int}/settle")]
    public async Task<IActionResult> SettleCommissions(int agentId)
    {
        var unsettled = await _db.AgentCommissions
            .Where(c => c.AgentId == agentId && c.SettledAt == null)
            .ToListAsync();

        if (!unsettled.Any())
            return BadRequest(new { error = "لا توجد عمولات غير مسوَّاة" });

        var now = DateTime.UtcNow;
        foreach (var c in unsettled)
            c.SettledAt = now;

        await _db.SaveChangesAsync();

        return Ok(new
        {
            SettledCount = unsettled.Count,
            TotalSettled = unsettled.Sum(c => c.CommissionAmount)
        });
    }

    [HttpGet("reports/agents")]
    public async Task<IActionResult> GetAgentsReport([FromQuery] int? month, [FromQuery] int? year)
    {
        var query = _db.AgentCommissions.AsQueryable();

        if (month.HasValue && year.HasValue)
        {
            var start = new DateTime(year.Value, month.Value, 1, 0, 0, 0, DateTimeKind.Utc);
            query = query.Where(c => c.CreatedAtUtc >= start && c.CreatedAtUtc < start.AddMonths(1));
        }

        var report = await query
            .Include(c => c.Agent)
            .GroupBy(c => new { c.AgentId, c.Agent!.Name })
            .Select(g => new
            {
                g.Key.AgentId,
                AgentName = g.Key.Name,
                OrderCount = g.Count(),
                TotalSales = g.Sum(c => c.SaleAmount),
                TotalCommission = g.Sum(c => c.CommissionAmount)
            })
            .OrderByDescending(x => x.TotalSales)
            .ToListAsync();

        // fallback من OrderAgentItems إذا لا توجد بيانات مسجلة
        if (report.Count == 0)
        {
            var aiQuery = _db.OrderAgentItems
                .Include(ai => ai.Agent)
                .Include(ai => ai.Order)
                .Where(ai => ai.Order!.CurrentStatus == AdminDashboard.Entities.OrderStatus.Delivered)
                .AsQueryable();

            if (month.HasValue && year.HasValue)
            {
                var start = new DateTime(year.Value, month.Value, 1, 0, 0, 0, DateTimeKind.Utc);
                var end = start.AddMonths(1);
                aiQuery = aiQuery.Where(ai =>
                    ai.Order!.DeliveredAtUtc.HasValue
                        ? ai.Order.DeliveredAtUtc >= start && ai.Order.DeliveredAtUtc < end
                        : ai.Order.CreatedAtUtc >= start && ai.Order.CreatedAtUtc < end);
            }

            var items = await aiQuery.ToListAsync();
            if (items.Any())
            {
                var fallback = items
                    .GroupBy(ai => new { ai.AgentId, Name = ai.Agent?.Name ?? $"مندوب #{ai.AgentId}" })
                    .Select(g =>
                    {
                        var its = g.ToList();
                        var sales = its.Sum(ai => ai.AgentSubtotal > 0 ? ai.AgentSubtotal : (ai.Order?.Total ?? 0m));
                        var comm = its.Sum(ai =>
                        {
                            var p = ai.CommissionPercent > 0 ? ai.CommissionPercent : (ai.Agent?.CommissionPercent ?? 0m);
                            var s = ai.AgentSubtotal > 0 ? ai.AgentSubtotal : (ai.Order?.Total ?? 0m);
                            return Math.Round(s * p / 100m, 2);
                        });
                        return new { AgentId = g.Key.AgentId, AgentName = g.Key.Name, OrderCount = its.Count, TotalSales = sales, TotalCommission = comm };
                    })
                    .OrderByDescending(x => x.TotalSales)
                    .ToList();
                return Ok(fallback);
            }
        }

        return Ok(report);
    }

    [HttpGet("reports/store")]
    public async Task<IActionResult> GetStoreReport([FromQuery] int? month, [FromQuery] int? year)
    {
        var now = DateTime.UtcNow;
        var start = month.HasValue && year.HasValue
            ? new DateTime(year.Value, month.Value, 1, 0, 0, 0, DateTimeKind.Utc)
            : new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);

        var orders = await _db.Orders
            .Where(o => o.CreatedAtUtc >= start && o.CreatedAtUtc < end && o.CurrentStatus == OrderStatus.Delivered)
            .ToListAsync();

        return Ok(new
        {
            Period = $"{start:yyyy-MM}",
            OrderCount = orders.Count,
            TotalRevenue = orders.Sum(o => o.Total),
            TotalDeliveryFees = orders.Sum(o => o.DeliveryFee),
            AverageOrderValue = orders.Count > 0 ? orders.Average(o => o.Total) : 0
        });
    }

    /// <summary>
    /// إصلاح العمولات المفقودة: يعالج الطلبات المُسلَّمة التي لا تحتوي على سجل عمولة
    /// </summary>
    [HttpPost("commissions/backfill")]
    public async Task<IActionResult> BackfillCommissions()
    {
        // جلب جميع الطلبات المُسلَّمة التي لها مندوب ولكن لا يوجد لها عمولة مسجلة
        var deliveredOrderIds = await _db.Orders
            .Where(o => o.CurrentStatus == OrderStatus.Delivered)
            .Select(o => o.Id)
            .ToListAsync();

        var ordersWithCommission = await _db.AgentCommissions
            .Select(c => c.OrderId)
            .Distinct()
            .ToListAsync();

        var missingOrderIds = deliveredOrderIds.Except(ordersWithCommission).ToList();

        if (!missingOrderIds.Any())
            return Ok(new { message = "لا توجد عمولات مفقودة", fixedCount = 0, checkedOrders = 0 });

        var agentItems = await _db.OrderAgentItems
            .Where(ai => missingOrderIds.Contains(ai.OrderId) && ai.AgentStatus != AgentOrderStatus.Rejected)
            .Include(ai => ai.Agent)
            .Include(ai => ai.Order)
            .ToListAsync();

        int fixedCount = 0;
        foreach (var agentItem in agentItems)
        {
            var existingCommission = await _db.AgentCommissions
                .AnyAsync(c => c.OrderId == agentItem.OrderId && c.AgentId == agentItem.AgentId);
            if (existingCommission) continue;

            var commissionPercent = agentItem.CommissionPercent > 0
                ? agentItem.CommissionPercent
                : (agentItem.Agent?.CommissionPercent ?? 0m);

            var saleAmount = agentItem.AgentSubtotal > 0
                ? agentItem.AgentSubtotal
                : (agentItem.Order?.Total ?? 0m);

            var commissionAmount = Math.Round(saleAmount * commissionPercent / 100m, 2);

            _db.AgentCommissions.Add(new AgentCommission
            {
                AgentId = agentItem.AgentId,
                OrderId = agentItem.OrderId,
                SaleAmount = saleAmount,
                CommissionPercent = commissionPercent,
                CommissionAmount = commissionAmount,
                CreatedAtUtc = agentItem.Order != null
                    ? (agentItem.Order.DeliveredAtUtc.HasValue ? agentItem.Order.DeliveredAtUtc.Value
                       : agentItem.Order.CreatedAtUtc)
                    : DateTime.UtcNow
            });
            fixedCount++;
        }

        await _db.SaveChangesAsync();
        return Ok(new { message = $"تم إصلاح {fixedCount} عمولة مفقودة", fixedCount = fixedCount, checkedOrders = missingOrderIds.Count });
    }
}
