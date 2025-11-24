namespace AnalyticsService.Models;

public class LinkHit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ShortCode { get; set; } = default!;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? UserAgent { get; set; }
    public string? Referrer { get; set; }
    public string? IpAddress { get; set; }
}