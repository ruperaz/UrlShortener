using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Redis
var redisConfig = builder.Configuration.GetSection("Redis")["Configuration"];
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConfig;
});

// HttpClient for LinkService
builder.Services.AddHttpClient("LinkService", client =>
{
    var baseUrl = builder.Configuration["Services:LinkServiceBaseUrl"]!;
    client.BaseAddress = new Uri(baseUrl);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", () => Results.Ok("RedirectService is running"));

// Actual redirect endpoint
app.MapGet("/{code}", async (string code, IDistributedCache cache, IHttpClientFactory httpClientFactory, HttpContext httpContext) =>
{
    if (string.IsNullOrWhiteSpace(code))
        return Results.BadRequest("Code is required.");

    // 1. Try Redis
    var cacheKey = $"shortlink:{code}";
    var cachedJson = await cache.GetStringAsync(cacheKey);

    string? targetUrl = null;
    bool isActive = true;
    DateTime? expiresAt = null;

    if (cachedJson is not null)
    {
        using var doc = JsonDocument.Parse(cachedJson);
        var root = doc.RootElement;
        targetUrl = root.GetProperty("OriginalUrl").GetString();
        if (root.TryGetProperty("IsActive", out var active))
            isActive = active.GetBoolean();
        if (root.TryGetProperty("ExpiresAt", out var exp) && exp.ValueKind == JsonValueKind.String)
            expiresAt = DateTime.Parse(exp.GetString()!);
    }
    else
    {
        // 2. Fallback to LinkService via internal API
        var client = httpClientFactory.CreateClient("LinkService");

        var response = await client.GetAsync($"/internal/links/by-code/{WebUtility.UrlEncode(code)}");
        if (!response.IsSuccessStatusCode)
        {
            return Results.NotFound("Short link not found.");
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        targetUrl = root.GetProperty("OriginalUrl").GetString();
        isActive = root.GetProperty("IsActive").GetBoolean();
        if (root.TryGetProperty("ExpiresAt", out var exp) && exp.ValueKind == JsonValueKind.String)
            expiresAt = DateTime.Parse(exp.GetString()!);
    }

    if (targetUrl is null) return Results.NotFound("Short link not found.");

    if (!isActive || (expiresAt.HasValue && expiresAt.Value < DateTime.UtcNow))
    {
        return Results.StatusCode(StatusCodes.Status410Gone); // Gone
    }

    // TODO: send analytics event (HTTP POST to AnalyticsService) - later

    return Results.Redirect(targetUrl, permanent: false);
});

app.Run();
