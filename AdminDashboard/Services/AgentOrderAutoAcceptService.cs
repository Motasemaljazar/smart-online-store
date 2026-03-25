using AdminDashboard.Data;
using AdminDashboard.Entities;
using Microsoft.EntityFrameworkCore;

namespace AdminDashboard.Services;

public class AgentOrderAutoAcceptService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentOrderAutoAcceptService> _logger;

    public AgentOrderAutoAcceptService(
        IServiceScopeFactory scopeFactory,
        ILogger<AgentOrderAutoAcceptService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessAutoAccepts(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in AgentOrderAutoAcceptService");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task ProcessAutoAccepts(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;

        var pendingItems = await db.OrderAgentItems
            .Include(x => x.Order)
                .ThenInclude(o => o!.Items)
            .Include(x => x.Agent)
            .Where(x => x.AgentStatus == AgentOrderStatus.Pending && x.AutoAcceptAt <= now)
            .ToListAsync(ct);

        if (!pendingItems.Any()) return;

        foreach (var item in pendingItems)
        {
            item.AgentStatus = AgentOrderStatus.AutoAccepted;
            item.RespondedAtUtc = now;

            // ملاحظة: المخزون يُخصم عند إنشاء الطلب من قِبل الزبون، لا نخصمه هنا مرة ثانية.

            _logger.LogInformation(
                "Auto-accepted OrderAgentItem {Id} for Agent {AgentId} on Order {OrderId}",
                item.Id, item.AgentId, item.OrderId);
        }

        await db.SaveChangesAsync(ct);
    }
}
