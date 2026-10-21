using System.Security.Cryptography;
using System.Text;
using Mentor.Mcp;
using Microsoft.Extensions.Options;

namespace Mentor.Middleware;

public sealed class McpAccessMiddleware(RequestDelegate next, IOptions<MentorMcpOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/mcp"))
        {
            var token = options.Value.Token;
            if (token == null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            var authorization = context.Request.Headers.Authorization.ToString();
            if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ||
                !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(token)),
                    SHA256.HashData(Encoding.UTF8.GetBytes(authorization[7..]))))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = "Bearer";
                return;
            }
        }
        await next(context);
    }
}
