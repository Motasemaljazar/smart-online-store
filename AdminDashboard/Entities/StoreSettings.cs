using System.ComponentModel.DataAnnotations;

namespace AdminDashboard.Entities;

public class StoreSettings
{
    public int Id { get; set; }

    [MaxLength(200)]
    public string StoreName { get; set; } = "";

    public string? LogoUrl { get; set; }
    public string? CustomerSplashUrl { get; set; }
    public string? DriverSplashUrl { get; set; }
    public string? SplashBackground1Url { get; set; }
    public string? SplashBackground2Url { get; set; }

    [MaxLength(16)]
    public string PrimaryColorHex { get; set; } = "#5C4A8E";

    [MaxLength(16)]
    public string SecondaryColorHex { get; set; } = "#111827";

    [MaxLength(16)]
    public string OffersColorHex { get; set; } = "#D4AF37";

    [MaxLength(200)]
    public string WelcomeText { get; set; } = "أهلاً بك";

    public string? OnboardingJson { get; set; }
    public string? HomeBannersJson { get; set; }

    [MaxLength(64)]
    public string WorkHours { get; set; } = "";

    public double StoreLat { get; set; }
    public double StoreLng { get; set; }

    public bool IsManuallyClosed { get; set; }

    [MaxLength(250)]
    public string ClosedMessage { get; set; } = "المتجر مغلق حالياً";

    public string? ClosedScreenImageUrl { get; set; }

    public decimal MinOrderAmount { get; set; }

    public DeliveryFeeType DeliveryFeeType { get; set; }
    public decimal DeliveryFeeValue { get; set; }

    public decimal DeliveryFeePerKm { get; set; }

    [MaxLength(64)]
    public string SupportPhone { get; set; } = "";

    [MaxLength(64)]
    public string SupportWhatsApp { get; set; } = "";

    [MaxLength(400)]
    public string? FacebookUrl { get; set; }

    [MaxLength(400)]
    public string? InstagramUrl { get; set; }

    [MaxLength(400)]
    public string? TelegramUrl { get; set; }

    public bool IsAcceptingOrders { get; set; }

    [MaxLength(16)]
    public string RoutingProfile { get; set; } = "driving";

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public decimal DriverSpeedBikeKmH { get; set; } = 18m;
    public decimal DriverSpeedCarKmH { get; set; } = 30m;

    /// <summary>تفعيل الرد التلقائي بالذكاء الاصطناعي على شكاوي الزبائن</summary>
    public bool AiAutoReplyEnabled { get; set; } = true;

    /// <summary>System prompt مخصص للرد التلقائي على الشكاوي</summary>
    public string? AiAutoReplySystemPrompt { get; set; }
}
