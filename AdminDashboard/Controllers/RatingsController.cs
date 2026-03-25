using AdminDashboard.Data;
using AdminDashboard.Entities;
using AdminDashboard.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AdminDashboard.Controllers;

[ApiController]
public class RatingsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHubContext<NotifyHub> _hub;
    private readonly NotificationService _notifications;

    public RatingsController(AppDbContext db, IHubContext<NotifyHub> hub, NotificationService notifications)
    {
        _db = db;
        _hub = hub;
        _notifications = notifications;
    }

    public record CreateOrderRatingRequest(int OrderId, int CustomerId, int StoreRate, int DriverRate, string? Comment);

    [HttpPost]
    [Route("api/ratings")]
    public async Task<IActionResult> Create(CreateOrderRatingRequest req)
    {
        if (req.StoreRate < 1 || req.StoreRate > 5)
            return BadRequest(new { error = "invalid_store_rate", message = "تقييم المتجر يجب أن يكون بين 1 و 5" });

        var o = await _db.Orders.FirstOrDefaultAsync(x => x.Id == req.OrderId);
        if (o == null) return NotFound(new { error = "not_found" });
        if (o.CustomerId != req.CustomerId) return Forbid();
        if (o.CurrentStatus != OrderStatus.Delivered)
        {
            // Allow rating if order was recently set to delivered but timestamp may not match
            var hasDeliveredHistory = await _db.OrderStatusHistory.AsNoTracking()
                .AnyAsync(h => h.OrderId == req.OrderId && h.Status == OrderStatus.Delivered);
            if (!hasDeliveredHistory)
                return BadRequest(new { error = "not_delivered", message = "يمكن التقييم بعد تسليم الطلب فقط" });
        }

        var existing = await _db.OrderRatings.FirstOrDefaultAsync(x => x.OrderId == o.Id);
        if (existing == null)
        {
            existing = new OrderRating
            {
                OrderId = o.Id,
                StoreRate = req.StoreRate,
                DriverRate = req.DriverRate > 0 ? req.DriverRate : (o.DriverId.HasValue ? 5 : 0),
                StoreComment = string.IsNullOrWhiteSpace(req.Comment) ? null : req.Comment.Trim(),
                Comment      = string.IsNullOrWhiteSpace(req.Comment) ? null : req.Comment.Trim(),
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.OrderRatings.Add(existing);
        }
        else
        {
            existing.StoreRate    = req.StoreRate;
            existing.DriverRate   = req.DriverRate;
            existing.StoreComment = string.IsNullOrWhiteSpace(req.Comment) ? null : req.Comment.Trim();
            existing.Comment      = string.IsNullOrWhiteSpace(req.Comment) ? null : req.Comment.Trim();
            existing.CreatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();

        await _hub.Clients.Group("admin").SendAsync("rating_added", new { orderId = o.Id, storeRate = existing.StoreRate, driverRate = existing.DriverRate, existing.CreatedAtUtc });
        await _hub.Clients.All.SendAsync("ratings_updated");

        await _notifications.CreateAndBroadcastAsync(NotificationUserType.Admin, null,
            "تقييم جديد", $"تم إضافة تقييم للطلب #{o.Id}", o.Id);

        return Ok(new { ok = true });
    }
}
