using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Franchise.Api.Auth;

/// <summary>Authorization filter for service-to-service calls: the
/// <c>X-Service-Key</c> header must match <c>SERVICE_API_KEY</c>. Anything else
/// (missing, wrong, even a valid user JWT) is a 401.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ServiceKeyAttribute : Attribute, IAuthorizationFilter
{
    public const string HeaderName = "X-Service-Key";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var expected = context.HttpContext.RequestServices
            .GetRequiredService<IConfiguration>()["SERVICE_API_KEY"];
        var provided = context.HttpContext.Request.Headers[HeaderName].ToString();

        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(provided) || !Matches(expected, provided))
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Invalid or missing service key" });
        }
    }

    // Hash both sides so the comparison is constant-time whatever the lengths.
    private static bool Matches(string expected, string provided) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
            SHA256.HashData(Encoding.UTF8.GetBytes(provided)));
}
