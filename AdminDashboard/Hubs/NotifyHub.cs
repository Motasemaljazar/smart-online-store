using Microsoft.AspNetCore.SignalR;

namespace AdminDashboard.Hubs;

public class NotifyHub : Hub
{
    public async Task JoinGroup(string groupType, int? userId = null)
    {
        var group = groupType switch
        {
            "admin"    => "admin",
            "customer" => userId.HasValue ? $"customer-{userId}" : null,
            "driver"   => userId.HasValue ? $"driver-{userId}"   : null,
            "agent"    => userId.HasValue ? $"agent-{userId}"     : null,
            _          => null
        };
        if (group != null)
            await Groups.AddToGroupAsync(Context.ConnectionId, group);
    }

    public async Task LeaveGroup(string groupType, int? userId = null)
    {
        var group = groupType switch
        {
            "admin"    => "admin",
            "customer" => userId.HasValue ? $"customer-{userId}" : null,
            "driver"   => userId.HasValue ? $"driver-{userId}"   : null,
            "agent"    => userId.HasValue ? $"agent-{userId}"     : null,
            _          => null
        };
        if (group != null)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
    }

    public Task JoinAdmin()              => Groups.AddToGroupAsync(Context.ConnectionId, "admin");
    public Task JoinCustomer(int customerId) => Groups.AddToGroupAsync(Context.ConnectionId, $"customer-{customerId}");
    public Task JoinAgent(int agentId)       => Groups.AddToGroupAsync(Context.ConnectionId, $"agent-{agentId}");

    public Task JoinDriver(string tokenOrId)
    {
        
        if (int.TryParse(tokenOrId, out var driverId))
            return Groups.AddToGroupAsync(Context.ConnectionId, $"driver-{driverId}");

        var parts = tokenOrId.Split(':');
        if (parts.Length >= 2 && int.TryParse(parts[0], out driverId))
            return Groups.AddToGroupAsync(Context.ConnectionId, $"driver-{driverId}");

        return Task.CompletedTask;
    }
}
