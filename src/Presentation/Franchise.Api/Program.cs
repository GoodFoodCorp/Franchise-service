using System.Text;
using Franchise.Api.Middleware;
using Franchise.Application;
using Franchise.Infrastructure;
using Franchise.Infrastructure.Bootstrap;
using Franchise.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
builder.Services.AddControllers();

// Shared HS256 secret issued by auth-service (see root README).
var jwtSecret = builder.Configuration["JWT_SECRET"]
    ?? throw new InvalidOperationException("JWT_SECRET is required.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
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
            db.Database.Migrate();
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

app.MapControllers();
app.MapHealthChecks("/readyz");
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();
