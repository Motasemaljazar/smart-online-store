using System.ComponentModel.DataAnnotations;

namespace AdminDashboard.Entities;

public class Offer
{
    public int Id { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = "";

    [MaxLength(2000)]
    public string? Description { get; set; }

    public string? ImageUrl { get; set; }

    public decimal? PriceBefore { get; set; }
    public decimal? PriceAfter { get; set; }

    [MaxLength(64)]
    public string? Code { get; set; }

    public DateTime? StartsAtUtc { get; set; }
    public DateTime? EndsAtUtc { get; set; }

    public bool IsActive { get; set; } = true;
}

public class OfferProduct
{
    public int Id { get; set; }
    public int OfferId { get; set; }
    public int ProductId { get; set; }
}

public class OfferCategory
{
    public int Id { get; set; }
    public int OfferId { get; set; }
    public int CategoryId { get; set; }
}

public enum DiscountTargetType
{
    Product = 1,
    Category = 2,
    Cart = 3
}

public enum DiscountValueType
{
    Percent = 1,
    Fixed = 2
}

public class Discount
{
    public int Id { get; set; }

    [MaxLength(120)]
    public string Title { get; set; } = "خصم";

    public DiscountTargetType TargetType { get; set; }

    public int? TargetId { get; set; }

    public DiscountValueType ValueType { get; set; } = DiscountValueType.Percent;

    public decimal? Percent { get; set; }

    public decimal? Amount { get; set; }

    public decimal? MinOrderAmount { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? StartsAtUtc { get; set; }
    public DateTime? EndsAtUtc { get; set; }

    [MaxLength(30)]
    public string? BadgeText { get; set; } 
}
