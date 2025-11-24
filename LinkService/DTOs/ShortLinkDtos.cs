namespace LinkService.DTOs;

public record CreateShortLinkRequest(
    string OriginalUrl,
    DateTime? ExpiresAt
);

public record ShortLinkResponse(
    Guid Id,
    string ShortCode,
    string OriginalUrl,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    bool IsActive,
    int ClickCount
);