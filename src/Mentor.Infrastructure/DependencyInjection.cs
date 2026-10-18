using Mentor.Application.Analyses;
using Mentor.Application.Configuration;
using Mentor.Application.Conversations;
using Mentor.Application.Diagnostics;
using Mentor.Application.Processing;
using Mentor.Infrastructure.Analyses;
using Mentor.Infrastructure.Configuration;
using Mentor.Infrastructure.Conversations;
using Mentor.Infrastructure.Hosting;
using Mentor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Mentor.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMentorInfrastructure(this IServiceCollection services,
        IConfiguration configuration, string contentRoot)
    {
        services.AddOptions<MentorInfrastructureOptions>()
            .Bind(configuration.GetSection("Mentor"))
            .PostConfigure(options => options.DataDirectory = Path.GetFullPath(options.DataDirectory, contentRoot))
            .Validate(options => !string.IsNullOrWhiteSpace(options.CodexExecutable), "CodexExecutable is required.")
            .Validate(options => options.AnalysisTimeout > TimeSpan.Zero && options.ImportTimeout > TimeSpan.Zero,
                "Timeouts must be positive.")
            .Validate(options => options.MaxImportBytes > 0, "Import limit must be positive.")
            .ValidateOnStart();

        services.AddSingleton<WorkspaceLease>();
        services.AddDbContextFactory<MentorDbContext>((provider, builder) =>
        {
            _ = provider.GetRequiredService<WorkspaceLease>();
            var options = provider.GetRequiredService<IOptions<MentorInfrastructureOptions>>().Value;
            builder.UseSqlite(MentorDbContext.ConnectionString(Path.Combine(options.DataDirectory, "mentor.db")))
                .EnableSensitiveDataLogging(false);
        });
        services.AddSingleton(provider =>
        {
            _ = provider.GetRequiredService<WorkspaceLease>();
            return new EfMentorRepository(provider.GetRequiredService<IDbContextFactory<MentorDbContext>>(),
                File.ReadAllText(Path.Combine(contentRoot, "prompts", "defaultMentorPrompt.txt")),
                provider.GetRequiredService<TimeProvider>());
        });
        services.AddSingleton<IConversationRepository>(p => p.GetRequiredService<EfMentorRepository>());
        services.AddSingleton<IAnalysisRepository>(p => p.GetRequiredService<EfMentorRepository>());
        services.AddSingleton<ISettingsRepository>(p => p.GetRequiredService<EfMentorRepository>());
        services.AddSingleton<IWorkQueue>(p => p.GetRequiredService<EfMentorRepository>());
        services.AddSingleton<IRepositoryDiagnostics>(p => p.GetRequiredService<EfMentorRepository>());

        services.AddHttpClient<IConversationImporter, ChatGptImporter>((provider, client) =>
        {
            client.Timeout = provider.GetRequiredService<IOptions<MentorInfrastructureOptions>>().Value.ImportTimeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mentor/1.0");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        services.AddSingleton<CodexCliAnalyzer>();
        services.AddSingleton<IConversationAnalyzer>(p => p.GetRequiredService<CodexCliAnalyzer>());
        services.AddSingleton<ICodexAvailability>(p => p.GetRequiredService<CodexCliAnalyzer>());
        return services;
    }
}
