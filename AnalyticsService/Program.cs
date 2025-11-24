using AnalyticsService.Data;
using AnalyticsService.Models;
using AnalyticsService.Consumers;
using Microsoft.EntityFrameworkCore;
using MassTransit;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AnalyticsDbContext>(options =>
{
    var cs = builder.Configuration.GetConnectionString("Postgres");
    options.UseNpgsql(cs);
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ===== RabbitMQ / MassTransit (Consumer) =====
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<LinkHitConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        var rabbitHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
        var rabbitUser = builder.Configuration["RabbitMQ:Username"] ?? "guest";
        var rabbitPass = builder.Configuration["RabbitMQ:Password"] ?? "guest";

        // Port MUST be ushort for this MassTransit version
        var rabbitPortStr = builder.Configuration["RabbitMQ:Port"];
        ushort rabbitPort = ushort.TryParse(rabbitPortStr, out var p) ? p : (ushort)5672;

        cfg.Host(rabbitHost, rabbitPort, "/", h =>
        {
            h.Username(rabbitUser);
            h.Password(rabbitPass);
        });

        cfg.ReceiveEndpoint("analytics-hit-queue", e =>
        {
            e.ConfigureConsumer<LinkHitConsumer>(context);
            e.UseMessageRetry(r => r.Interval(5, TimeSpan.FromSeconds(2)));
        });
    });

});

var app = builder.Build();

// migrate db
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", () => Results.Ok("AnalyticsService is running"));

// keep your internal HTTP endpoint if you want (fine for testing)
app.MapPost("/internal/analytics/hit", async (LinkHit hit, AnalyticsDbContext db) =>
{
    hit.Id = Guid.NewGuid();
    hit.Timestamp = DateTime.UtcNow;

    db.LinkHits.Add(hit);
    await db.SaveChangesAsync();

    return Results.Accepted();
});

app.MapGet("/api/analytics/{code}", async (string code, AnalyticsDbContext db) =>
{
    var hits = await db.LinkHits
        .Where(x => x.ShortCode == code)
        .OrderByDescending(x => x.Timestamp)
        .Take(100)
        .ToListAsync();

    return Results.Ok(hits);
});

app.Run();
