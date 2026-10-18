using System.Net;

namespace Mentor.Middleware;

public sealed class LocalAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var host = context.Request.Host;
        if (context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip) ||
            host.Port != 5178 || host.Host is not ("127.0.0.1" or "localhost" or "[::1]"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (context.Request.Method is not ("GET" or "HEAD") &&
            (context.Request.Headers["X-Mentor-Request"] != "1" ||
             (context.Request.Headers.TryGetValue("Origin", out var origin) && origin != $"http://{host}")))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
        await next(context);
    }
}
