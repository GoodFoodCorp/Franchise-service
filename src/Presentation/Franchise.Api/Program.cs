using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using Franchise.Api;
using Franchise.Api.Controllers;
using Franchise.Api.Middleware;
using Franchise.Application;
using Franchise.Infrastructure;
using Franchise.Infrastructure.Bootstrap;
using Franchise.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((_, cfg) => cfg
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("service", "franchise-service")
    .WriteTo.Console(new CompactJsonFormatter()));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
// Restaurants created before geocoding (or while it was down) get their coordinates.
builder.Services.AddHostedService<RestaurantLocationBackfill>();
builder.Services.AddControllers();

// The client address behind the ingress / gateway (private networks only),
// so per-client limits apply to the customer, not to the proxy.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
    foreach (var cidr in new[] { "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8" })
    {
        var parts = cidr.Split('/');
        o.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse(parts[0]), int.Parse(parts[1])));
    }
});

// Address autocomplete calls an external provider: 60 lookups a minute per
// client is plenty for a person typing (debounced) and stops scripted abuse.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(RestaurantsController.AddressSuggestionsPolicy, http =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
});

// Shared HS256 secret issued by auth-service (see root README).
var jwtSecret = builder.Configuration["JWT_SECRET"]
    ?? throw new InvalidOperationException("JWT_SECRET is required.");
var jwtIssuer = builder.Configuration["JWT_ISSUER"] ?? "goodfood-auth";
var jwtAudience = builder.Configuration["JWT_AUDIENCE"] ?? "goodfood-api";

// Shared secret for service-to-service calls on /internal/... (X-Service-Key header).
if (string.IsNullOrWhiteSpace(builder.Configuration["SERVICE_API_KEY"]))
{
    throw new InvalidOperationException("SERVICE_API_KEY is required.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = "role_slugs",
            NameClaimType = "sub",
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                if (string.IsNullOrEmpty(ctx.Token) &&
                    ctx.Request.Cookies.TryGetValue("auth_token", out var cookie))
                {
                    ctx.Token = cookie;
                }

                return Task.CompletedTask;
            },
            // Only access tokens open the API: refresh tokens (typ=refresh,
            // signed with another key anyway) and untyped tokens are refused.
            OnTokenValidated = ctx =>
            {
                if (ctx.Principal?.FindFirst("typ")?.Value != "access")
                {
                    ctx.Fail("Not an access token.");
                }

                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Franchise Service API",
        Version = "v1",
        Description = "Good Food 3.0 — restaurants (franchises) and their suppliers.",
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
            },
            Array.Empty<string>()
        },
    });
});

builder.Services.AddHealthChecks().AddDbContextCheck<FranchiseDbContext>("database");

var app = builder.Build();

// Migrate, then import restaurants from auth-service preserving their ids.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FranchiseDbContext>();
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await LockedMigrator.MigrateAsync(db);
            break;
        }
        catch (Exception ex) when (attempt < 15)
        {
            app.Logger.LogWarning("Database not ready (attempt {Attempt}/15): {Error}", attempt, ex.Message);
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    var importer = scope.ServiceProvider.GetRequiredService<TenantImporter>();
    var authUrl = builder.Configuration["AUTH_SERVICE_URL"] ?? "http://auth-service:8081";
    await importer.ImportAsync(authUrl);
}

app.UseForwardedHeaders();
app.UseMiddleware<RequestIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseSerilogRequestLogging();

app.UseSwagger(c => c.RouteTemplate = "docs/{documentName}/openapi.json");
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/docs/v1/openapi.json", "Franchise Service v1");
    c.RoutePrefix = "docs";
});

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHealthChecks("/readyz");
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();
