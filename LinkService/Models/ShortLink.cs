namespace LinkService.Models;

public class ShortLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ShortCode { get; set; } = default!;
    public string OriginalUrl { get; set; } = default!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
    public int ClickCount { get; set; } = 0;
    public string? OwnerUserId { get; set; } // from Identity service (string to hold GUID/whatever)
}