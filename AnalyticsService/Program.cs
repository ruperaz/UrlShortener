using AnalyticsService.Data;
using AnalyticsService.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AnalyticsDbContext>(options =>
{
    var cs = builder.Configuration.GetConnectionString("Postgres");
    options.UseNpgsql(cs);
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

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

// Endpoint to record a hit (called from RedirectService later)
app.MapPost("/internal/analytics/hit", async (LinkHit hit, AnalyticsDbContext db) =>
{
    hit.Id = Guid.NewGuid();
    hit.Timestamp = DateTime.UtcNow;

    db.LinkHits.Add(hit);
    await db.SaveChangesAsync();

    return Results.Accepted();
});

// Basic query: hits by short code
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