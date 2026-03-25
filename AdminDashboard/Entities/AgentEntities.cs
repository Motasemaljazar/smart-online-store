using System.ComponentModel.DataAnnotations;

namespace AdminDashboard.Entities;

public class OrderAgentItem
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order? Order { get; set; }

    public int AgentId { get; set; }
    public Agent? Agent { get; set; }

    public AgentOrderStatus AgentStatus { get; set; } = AgentOrderStatus.Pending;

    public DateTime AutoAcceptAt { get; set; }

    [MaxLength(500)]
    public string? RejectionReason { get; set; }

    public decimal CommissionPercent { get; set; }

    public decimal AgentSubtotal { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedAtUtc { get; set; }
}

public class AgentCommission
{
    public int Id { get; set; }

    public int AgentId { get; set; }
    public Agent? Agent { get; set; }

    public int OrderId { get; set; }
    public Order? Order { get; set; }

    public decimal SaleAmount { get; set; }
    public decimal CommissionPercent { get; set; }
    public decimal CommissionAmount { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? SettledAt { get; set; }
}

public class ProductAgentChat
{
    public int Id { get; set; }

    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public int AgentId { get; set; }
    public Agent? Agent { get; set; }

    public int? ProductId { get; set; }
    public Product? Product { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<ProductAgentChatMessage> Messages { get; set; } = new();
}

public class ProductAgentChatMessage
{
    public int Id { get; set; }

    public int ThreadId { get; set; }
    public ProductAgentChat? Thread { get; set; }

    public bool FromAgent { get; set; }

    [MaxLength(2000)]
    public string Message { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
