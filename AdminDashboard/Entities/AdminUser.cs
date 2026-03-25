using System.ComponentModel.DataAnnotations;

namespace AdminDashboard.Entities;

public class AdminUser
{
    public int Id { get; set; }

    [MaxLength(200)]
    public string Email { get; set; } = "admin";

    [MaxLength(400)]
    public string PasswordHash { get; set; } = "";

    [MaxLength(200)]
    public string PasswordSalt { get; set; } = "";

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
