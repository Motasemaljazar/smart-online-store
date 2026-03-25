using Microsoft.AspNetCore.SignalR;

namespace AdminDashboard.Hubs;

public class TrackingHub : Hub
{
    
    public Task JoinAdmin() => Groups.AddToGroupAsync(Context.ConnectionId, "admin");

    public Task JoinCustomer(int customerId) => Groups.AddToGroupAsync(Context.ConnectionId, $"customer-{customerId}");

    public Task JoinDriver(int driverId) => Groups.AddToGroupAsync(Context.ConnectionId, $"driver-{driverId}");

    public Task WatchDriver(int driverId)   => Groups.AddToGroupAsync(Context.ConnectionId, $"track-driver-{driverId}");
    public Task UnwatchDriver(int driverId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"track-driver-{driverId}");
}
