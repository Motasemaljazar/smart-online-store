using System.ComponentModel.DataAnnotations;

namespace AdminDashboard.Entities;

public class Category
{
    public int Id { get; set; }

    [MaxLength(120)]
    public string Name { get; set; } = "";

    [MaxLength(400)]
    public string? ImageUrl { get; set; }

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; } = 0;

    public List<Product> Products { get; set; } = new();
}

public class Product
{
    public int Id { get; set; }

    [MaxLength(200)]
    public string Name { get; set; } = "";

    [MaxLength(2000)]
    public string? Description { get; set; }

    public decimal Price { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsAvailable { get; set; } = true;

    [MaxLength(400)]
    public string? ImageUrl { get; set; }

    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    public int? AgentId { get; set; }
    public Agent? Agent { get; set; }

    public int StockQuantity { get; set; } = 0;

    public bool TrackStock { get; set; } = false;

    public List<ProductImage> Images { get; set; } = new();
    public List<ProductVariant> Variants { get; set; } = new();
    public List<ProductAddon> Addons { get; set; } = new();
}

public class ProductImage
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }

    [MaxLength(400)]
    public string Url { get; set; } = "";

    public int SortOrder { get; set; }
    public bool IsPrimary { get; set; }
}
