using AdminDashboard.Entities;
using AdminDashboard.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace AdminDashboard.Data;

public class NotificationService
{
    private readonly AppDbContext _db;
    private readonly IHubContext<NotifyHub> _hub;

    public NotificationService(AppDbContext db, IHubContext<NotifyHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    public async Task CreateAndBroadcastAsync(NotificationUserType userType, int? userId, string title, string body, int? relatedOrderId = null)
    {
        var n = new Notification
        {
            UserType = userType,
            UserId = userId,
            Title = title,
            Body = body,
            RelatedOrderId = relatedOrderId,
            IsRead = false,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.Notifications.Add(n);
        await _db.SaveChangesAsync();

        var payload = new { n.Id, userType = n.UserType, n.UserId, n.Title, n.Body, n.RelatedOrderId, n.IsRead, n.CreatedAtUtc };

        if (userType == NotificationUserType.Admin)
            await _hub.Clients.Group("admin").SendAsync("notification", payload);
        else if (userType == NotificationUserType.Customer && userId != null)
            await _hub.Clients.Group($"customer-{userId}").SendAsync("notification", payload);
        else if (userType == NotificationUserType.Driver && userId != null)
            await _hub.Clients.Group($"driver-{userId}").SendAsync("notification", payload);
        else if (userType == NotificationUserType.Agent && userId != null)
            await _hub.Clients.Group($"agent-{userId}").SendAsync("notification", payload);
    }

    public Task SendCustomerOrderStatusPushIfNeededAsync(int customerId, int orderId, OrderStatus status, int? prepEtaMinutes = null, int? deliveryEtaMinutes = null)
        => Task.CompletedTask;

    public Task SendCustomerEtaUpdatedPushAsync(int customerId, int orderId, int? prepEtaMinutes, int? deliveryEtaMinutes)
        => Task.CompletedTask;

    public Task SendAdminChatPushAsync(int? relatedOrderId, int customerId, string? message)
        => Task.CompletedTask;

    public Task SendCustomerChatPushAsync(int customerId, int? relatedOrderId, string? message)
        => Task.CompletedTask;
}
