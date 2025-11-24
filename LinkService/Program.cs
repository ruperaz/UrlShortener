using System.Security.Claims;
using System.Text;
using System.Text.Json;
using LinkService.Data;
using LinkService.DTOs;
using LinkService.Models;
using LinkService.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// DbContext
builder.Services.AddDbContext<LinkDbContext>(options =>
{
    var cs = builder.Configuration.GetConnectionString("Postgres");
    options.UseNpgsql(cs);
});

// Redis caching
var redisConfig = builder.Configuration.GetSection("Redis")["Configuration"];
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConfig;
});

// Swagger + JWT auth in Swagger UI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "LinkService API",
        Version = "v1"
    });

    // JWT scheme
    var securityScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "Enter JWT with **Bearer** prefix. Example: `Bearer {token}`",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Reference = new OpenApiReference
        {
            Type = ReferenceType.SecurityScheme,
            Id = "Bearer"
        }
    };

    c.AddSecurityDefinition("Bearer", securityScheme);

    var securityRequirement = new OpenApiSecurityRequirement
    {
        { securityScheme, Array.Empty<string>() }
    };

    c.AddSecurityRequirement(securityRequirement);
});

// JWT config – must match IdentityService
var jwtSection = builder.Configuration.GetSection("Jwt");
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["SigningKey"]!));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.FromMinutes(2)
        };

        // Optional: log token validation issues
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                Console.WriteLine("JWT auth failed in LinkService:");
                Console.WriteLine(context.Exception.ToString());
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Auto-migrate (dev only)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LinkDbContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok("LinkService is running"));

// All link APIs require auth
var linksGroup = app.MapGroup("/api/links").RequireAuthorization();

// Create Short Link
linksGroup.MapPost("/", async (CreateShortLinkRequest request, LinkDbContext db, IDistributedCache cache, ClaimsPrincipal user) =>
{
    if (string.IsNullOrWhiteSpace(request.OriginalUrl))
        return Results.BadRequest("OriginalUrl is required.");

    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? user.FindFirstValue(ClaimTypes.Name)
                 ?? "anonymous";

    var shortCode = ShortCodeGenerator.Generate();

    // ensure uniqueness
    for (int i = 0; i < 5; i++)
    {
        if (!await db.ShortLinks.AnyAsync(x => x.ShortCode == shortCode))
            break;

        shortCode = ShortCodeGenerator.Generate();
    }

    var entity = new ShortLink
    {
        ShortCode = shortCode,
        OriginalUrl = request.OriginalUrl,
        ExpiresAt = request.ExpiresAt,
        OwnerUserId = userId
    };

    db.ShortLinks.Add(entity);
    await db.SaveChangesAsync();

    var response = ToResponse(entity);

    var json = JsonSerializer.Serialize(response);
    await cache.SetStringAsync(GetCacheKey(shortCode), json);

    return Results.Created($"/api/links/{entity.Id}", response);
});

// Get current user's links
linksGroup.MapGet("/", async (ClaimsPrincipal user, int page, int pageSize, LinkDbContext db) =>
{
    page = page < 1 ? 1 : page;
    pageSize = pageSize < 1 ? 20 : pageSize;

    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? user.FindFirstValue(ClaimTypes.Name)
                 ?? "anonymous";

    var query = db.ShortLinks
        .Where(x => x.OwnerUserId == userId)
        .OrderByDescending(x => x.CreatedAt);

    var entities = await query
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();

    var items = entities.Select(ToResponse).ToList();

    return Results.Ok(items);
});

// Get single link if owned by user
linksGroup.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, LinkDbContext db) =>
{
    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? user.FindFirstValue(ClaimTypes.Name)
                 ?? "anonymous";

    var entity = await db.ShortLinks.FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == userId);
    if (entity is null) return Results.NotFound();

    return Results.Ok(ToResponse(entity));
});

// Update link if owner
linksGroup.MapPut("/{id:guid}", async (Guid id, CreateShortLinkRequest request, ClaimsPrincipal user, LinkDbContext db, IDistributedCache cache) =>
{
    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? user.FindFirstValue(ClaimTypes.Name)
                 ?? "anonymous";

    var entity = await db.ShortLinks.FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == userId);
    if (entity is null) return Results.NotFound();

    entity.OriginalUrl = request.OriginalUrl;
    entity.ExpiresAt = request.ExpiresAt;

    await db.SaveChangesAsync();

    await cache.RemoveAsync(GetCacheKey(entity.ShortCode));

    return Results.NoContent();
});

// Delete link if owner
linksGroup.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, LinkDbContext db, IDistributedCache cache) =>
{
    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? user.FindFirstValue(ClaimTypes.Name)
                 ?? "anonymous";

    var entity = await db.ShortLinks.FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == userId);
    if (entity is null) return Results.NotFound();

    db.ShortLinks.Remove(entity);
    await db.SaveChangesAsync();

    await cache.RemoveAsync(GetCacheKey(entity.ShortCode));

    return Results.NoContent();
});

// Internal – used by RedirectService (no auth)
app.MapGet("/internal/links/by-code/{code}", async (string code, LinkDbContext db) =>
{
    var entity = await db.ShortLinks.FirstOrDefaultAsync(x => x.ShortCode == code);
    if (entity is null) return Results.NotFound();

    return Results.Ok(new
    {
        entity.ShortCode,
        entity.OriginalUrl,
        entity.ExpiresAt,
        entity.IsActive
    });
});

static string GetCacheKey(string shortCode) => $"shortlink:{shortCode}";

static ShortLinkResponse ToResponse(ShortLink entity) =>
    new(
        entity.Id,
        entity.ShortCode,
        entity.OriginalUrl,
        entity.CreatedAt,
        entity.ExpiresAt,
        entity.IsActive,
        entity.ClickCount
    );

app.Run();
