using Yarp.ReverseProxy;
using Swashbuckle.AspNetCore.SwaggerUI;

var builder = WebApplication.CreateBuilder(args);

// YARP reverse proxy – config comes from appsettings.json
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// NOTE: no AddSwaggerGen / UseSwagger here –
// gateway does NOT have its own swagger.json, it only hosts Swagger UI.

var app = builder.Build();

// Swagger UI as an aggregator for downstream services
app.UseSwaggerUI(c =>
{
    // These are proxied through the gateway
    c.SwaggerEndpoint("/identity/swagger/v1/swagger.json", "Identity API v1");
    c.SwaggerEndpoint("/links/swagger/v1/swagger.json", "Link API v1");
    c.SwaggerEndpoint("/analytics/swagger/v1/swagger.json", "Analytics API v1");

    c.RoutePrefix = "swagger";          // UI at /swagger
    c.DocExpansion(DocExpansion.List);  // Optional: expand operations by default
});

app.MapGet("/", () => "Gateway is running");

// Let YARP handle all proxied routes
app.MapReverseProxy();

app.Run();