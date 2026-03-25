namespace AdminDashboard.Entities;

public class DriverTrackPoint
{
    public int Id { get; set; }
    public int DriverId { get; set; }

    public int? OrderId { get; set; }

    public double Lat { get; set; }
    public double Lng { get; set; }
    public double SpeedMps { get; set; }
    public double HeadingDeg { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
