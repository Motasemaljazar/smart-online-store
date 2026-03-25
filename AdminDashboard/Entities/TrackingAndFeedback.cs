using System.ComponentModel.DataAnnotations;

namespace AdminDashboard.Entities;

public class DriverLocation
{
    public int Id { get; set; }
    public int DriverId { get; set; }
    public Driver? Driver { get; set; }

    public double Lat { get; set; }
    public double Lng { get; set; }
    public double SpeedMps { get; set; }
    public double HeadingDeg { get; set; }
    
    public double AccuracyMeters { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class Rating
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int DriverId { get; set; }
    public int CustomerId { get; set; }

    public int Stars { get; set; }

    [MaxLength(800)]
    public string? Comment { get; set; }

    public int? StoreStars { get; set; }

    [MaxLength(800)]
    public string? StoreComment { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class OrderRating
{
    
    [Key]
    public int OrderId { get; set; }

    public int StoreRate { get; set; }
    public int DriverRate { get; set; }

    // حقل قديم للتوافق مع البيانات السابقة
    [MaxLength(800)]
    public string? Comment { get; set; }

    // تعليق تقييم المتجر
    [MaxLength(800)]
    public string? StoreComment { get; set; }

    // تعليق تقييم السائق
    [MaxLength(800)]
    public string? DriverComment { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class ComplaintThread
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int? OrderId { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? LastAdminSeenAtUtc { get; set; }
    public DateTime? LastCustomerSeenAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsArchivedByAdmin { get; set; } = false;

    public List<ComplaintMessage> Messages { get; set; } = new();
}

public class ComplaintMessage
{
    public int Id { get; set; }
    public int ThreadId { get; set; }
    public ComplaintThread? Thread { get; set; }

    public bool FromAdmin { get; set; }

    [MaxLength(2000)]
    public string Message { get; set; } = "";

    [MaxLength(120)]
    public string IdempotencyKey { get; set; } = Guid.NewGuid().ToString("N");

    public ComplaintMessage() { }

    public ComplaintMessage(int threadId, bool fromAdmin, string message)
    {
        ThreadId = threadId;
        FromAdmin = fromAdmin;
        Message = message;
        IdempotencyKey = Guid.NewGuid().ToString("N");
    }

    public ComplaintMessage(int threadId, bool fromAdmin, string message, string idempotencyKey)
    {
        ThreadId = threadId;
        FromAdmin = fromAdmin;
        Message = message;
        IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey)
            ? Guid.NewGuid().ToString("N")
            : idempotencyKey;
    }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class CustomerFavorite
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class ProductRating
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int OrderId { get; set; }
    public int Stars { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(800)]
    public string? Comment { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
