using MassTransit;
using AnalyticsService.Data;
using AnalyticsService.Models;
using ShortLink.Contracts;

namespace AnalyticsService.Consumers;

public class LinkHitConsumer : IConsumer<LinkHitEvent>
{
    private readonly AnalyticsDbContext _db;

    public LinkHitConsumer(AnalyticsDbContext db)
    {
        _db = db;
    }

    public async Task Consume(ConsumeContext<LinkHitEvent> context)
    {
        var msg = context.Message;

        var hit = new LinkHit
        {
            Id = Guid.NewGuid(),
            ShortCode = msg.ShortCode,
            IpAddress = msg.Ip,
            UserAgent = msg.UserAgent,
            Referrer = msg.Referer,
            Timestamp = msg.TimestampUtc
        };

        _db.LinkHits.Add(hit);
        await _db.SaveChangesAsync();
    }
}