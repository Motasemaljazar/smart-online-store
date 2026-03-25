namespace AdminDashboard.Services;

public class FcmService
{
    public Task RegisterTokenAsync(object userType, int userId, string token, string? platform) => Task.CompletedTask;
    public Task UnregisterTokenAsync(string token) => Task.CompletedTask;
    public Task SendToTopicAsync(string topic, string title, string body, Dictionary<string, string>? data = null) => Task.CompletedTask;
    public Task SendToDeviceAsync(string token, string title, string body, Dictionary<string, string>? data = null) => Task.CompletedTask;
    public Task SendToUserAsync(object userType, int userId, string title, string body, Dictionary<string, string>? data = null) => Task.CompletedTask;
}
