using Franchise.Domain.Errors;

namespace Franchise.Api.Middleware;

/// <summary>Maps typed domain errors to HTTP status codes.</summary>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (DomainException ex)
        {
            context.Response.StatusCode = ex.Code switch
            {
                DomainErrorCode.Validation => StatusCodes.Status400BadRequest,
                DomainErrorCode.NotFound => StatusCodes.Status404NotFound,
                DomainErrorCode.Forbidden => StatusCodes.Status403Forbidden,
                DomainErrorCode.Conflict => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status500InternalServerError,
            };
            await context.Response.WriteAsJsonAsync(new
            {
                error = ex.Message,
                request_id = context.Response.Headers["X-Request-ID"].ToString(),
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = "Internal server error" });
        }
    }
}

/// <summary>Propagates or creates the X-Request-ID correlation header.</summary>
public sealed class RequestIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = context.Request.Headers["X-Request-ID"].FirstOrDefault() ?? Guid.NewGuid().ToString();
        context.Response.Headers["X-Request-ID"] = requestId;
        using (Serilog.Context.LogContext.PushProperty("request_id", requestId))
        {
            await next(context);
        }
    }
}
