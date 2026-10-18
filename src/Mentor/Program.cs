using Mentor.Application.Dashboard;
using Mentor.Application.Diagnostics;
using Mentor.Application.Processing;
using Mentor.Endpoints;
using Mentor.Hosting;
using Mentor.Infrastructure;
using Mentor.Middleware;
using Mentor.Observability;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});
builder.WebHost.UseUrls("http://127.0.0.1:5178");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 2 * 1024 * 1024);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<MentorTelemetry>();
builder.Services.AddScoped<MentorService>();
builder.Services.AddScoped<WorkProcessor>();
builder.Services.AddSingleton<WorkerStatus>();
builder.Services.AddHostedService<MentorWorker>();
builder.Services.AddMentorInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);
builder.AddMentorObservability();

var app = builder.Build();
app.UseMiddleware<LocalAccessMiddleware>();
app.UseSerilogRequestLogging(options =>
{
    options.GetLevel = (context, _, error) => error != null || context.Response.StatusCode >= 500
        ? LogEventLevel.Error
        : ObservabilityExtensions.IsPoll(context.Request.Path) ? LogEventLevel.Debug : LogEventLevel.Information;
});
app.UseMiddleware<ApiErrorMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapMentorApi();
app.MapMentorOperations();
app.Run();

public partial class Program;
