using System.Diagnostics;
using Mentor.Application.Analyses;

namespace Mentor.Middleware;

public sealed class ApiErrorMiddleware(RequestDelegate next, ILogger<ApiErrorMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception error) when (!context.Response.HasStarted)
        {
            var statusCode = error switch
            {
                ArgumentException => StatusCodes.Status400BadRequest,
                System.Text.Json.JsonException => StatusCodes.Status400BadRequest,
                AnalysisConflictException => StatusCodes.Status409Conflict,
                BadHttpRequestException badRequest => badRequest.StatusCode,
                _ => StatusCodes.Status500InternalServerError
            };
            var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
            if (statusCode >= 500)
                logger.LogError("HTTP request failed with {ErrorType} ({TraceId})", error.GetType().Name, traceId);

            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsJsonAsync(new
            {
                error = statusCode >= 500 ? "Došlo k interní chybě. Podrobnosti najdete v provozních záznamech." : error.Message,
                traceId
            }, context.RequestAborted);
        }
    }
}
