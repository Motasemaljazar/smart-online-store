using System.ComponentModel.DataAnnotations;

namespace AdminDashboard.Entities;

public class Order
{
    public int Id { get; set; }

    [MaxLength(64)]
    public string? IdempotencyKey { get; set; }

    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public int? DriverId { get; set; }
    public Driver? Driver { get; set; }

    public OrderStatus CurrentStatus { get; set; } = OrderStatus.New;

    [MaxLength(80)]
    public string? CancelReasonCode { get; set; }

    public int? CustomerAddressId { get; set; }
    public CustomerAddress? CustomerAddress { get; set; }

    public double DeliveryLat { get; set; }
    public double DeliveryLng { get; set; }
    public string? DeliveryAddress { get; set; }

    [MaxLength(800)]
    public string? Notes { get; set; }

    public decimal Subtotal { get; set; }
    public decimal DeliveryFee { get; set; }

    public decimal TotalBeforeDiscount { get; set; }

    public decimal CartDiscount { get; set; }

    public decimal Total { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? OrderEditableUntilUtc { get; set; }

    public DateTime? DriverConfirmedAtUtc { get; set; }

    public DateTime? DeliveredAtUtc { get; set; }

    public double DeliveryDistanceKm { get; set; }

    public double DistanceKm { get; set; } = 0;

    public int? ProcessingEtaMinutes { get; set; }
    public int? DeliveryEtaMinutes { get; set; }
    public DateTime? ExpectedDeliveryAtUtc { get; set; }
    public DateTime? LastEtaUpdatedAtUtc { get; set; }

    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

    public List<OrderItem> Items { get; set; } = new();
    public List<OrderStatusHistory> StatusHistory { get; set; } = new();
}

public class OrderItem
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order? Order { get; set; }

    public int ProductId { get; set; }
    public string ProductNameSnapshot { get; set; } = "";
    public decimal UnitPriceSnapshot { get; set; }
    public int Quantity { get; set; }

    [MaxLength(400)]
    public string? OptionsSnapshot { get; set; }
}

public class OrderStatusHistory
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }

    public OrderStatus Status { get; set; }
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(20)]
    public string? ChangedByType { get; set; } 
    public int? ChangedById { get; set; }

    [MaxLength(80)]
    public string? ReasonCode { get; set; }

    [MaxLength(200)]
    public string? Comment { get; set; }
}

