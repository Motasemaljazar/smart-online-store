using System.ComponentModel.DataAnnotations;

namespace AdminDashboard.Entities;

public class Agent
{
    public int Id { get; set; }

    [MaxLength(120)]
    public string Name { get; set; } = "";

    [MaxLength(40)]
    public string Phone { get; set; } = "";

    [MaxLength(16)]
    public string Pin { get; set; } = "1234";

    [MaxLength(256)]
    public string? PasswordHash { get; set; }

    [MaxLength(180)]
    public string? Email { get; set; }

    public AgentStatus Status { get; set; }

    public string? PhotoUrl { get; set; }

    public decimal CommissionPercent { get; set; } = 0;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<OrderAgentItem> OrderAgentItems { get; set; } = new();
    public List<AgentCommission> Commissions { get; set; } = new();
}
