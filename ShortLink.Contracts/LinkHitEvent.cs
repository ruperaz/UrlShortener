namespace ShortLink.Contracts;

public record LinkHitEvent(
    string ShortCode,
    string? Ip,
    string? UserAgent,
    string? Referer,
    DateTime TimestampUtc
);