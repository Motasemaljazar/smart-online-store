using System.ComponentModel.DataAnnotations;

namespace AdminDashboard.Entities;

public class Customer
{
    public int Id { get; set; }

    [MaxLength(120)]
    public string Name { get; set; } = "";

    [MaxLength(40)]
    public string Phone { get; set; } = "";

    [MaxLength(180)]
    public string? Email { get; set; }

    public double DefaultLat { get; set; }
    public double DefaultLng { get; set; }
    public string? DefaultAddress { get; set; }

    public double LastLat { get; set; }
    public double LastLng { get; set; }

    public bool IsChatBlocked { get; set; } = false;

    public bool IsAppBlocked { get; set; } = false;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
