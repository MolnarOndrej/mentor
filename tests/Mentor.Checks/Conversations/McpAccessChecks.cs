using System.Net;
using Mentor.Mcp;
using Mentor.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Mentor.Checks.Conversations;

internal static class McpAccessChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var token = new string('a', 64);
        check(new MentorMcpOptions().IsValid() && new MentorMcpOptions { Token = token }.IsValid() &&
            !new MentorMcpOptions { Token = "" }.IsValid() &&
            !new MentorMcpOptions { Token = new string('a', 31) }.IsValid() &&
            !new MentorMcpOptions { Token = new string('a', 257) }.IsValid() &&
            !new MentorMcpOptions { Token = token + "\n" }.IsValid(),
            "MCP defaults to disabled and rejects short, empty or non-header-safe configured tokens");
        var reached = false;
        var access = new McpAccessMiddleware(_ => { reached = true; return Task.CompletedTask; },
            Options.Create(new MentorMcpOptions { Token = token }));
        var local = new LocalAccessMiddleware(access.InvokeAsync);
        foreach (var ip in new[] { IPAddress.Loopback, IPAddress.IPv6Loopback, IPAddress.Parse("192.0.2.1") })
        {
            reached = false;
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = ip;
            context.Request.Host = new HostString("127.0.0.1", 5178);
            context.Request.Path = "/mcp";
            context.Request.Method = "POST";
            context.Request.Headers.Authorization = "Bearer " + token;
            await local.InvokeAsync(context);
            check(reached == IPAddress.IsLoopback(ip), "MCP bearer authentication never bypasses loopback restriction: " + ip);
        }
    }
}
