using Mentor.Application.Analyses;
using Mentor.Application.Configuration;
using Mentor.Application.Conversations;
using Mentor.Application.Discussions;
using Mentor.Application.Processing;
using Mentor.Application.Prompts;

namespace Mentor.Application.Dashboard;

public sealed class MentorService(
    IConversationRepository conversations,
    IAnalysisRepository analyses,
    ISettingsRepository settings,
    IWorkQueue queue,
    IConversationImporter importer,
    TimeProvider clock,
    ConversationRefresher refresher)
{
    public MentorState GetState()
    {
        var reports = analyses.Analyses();
        var coverage = reports.Where(a => a.Status == "completed")
            .GroupBy(a => a.Day)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.Id).First().MessageKeys.ToHashSet());

        var stored = conversations.Conversations();
        var summaries = stored.Select(c =>
        {
            var day = DailySchedule.Day(DateTimeOffset.Parse(c.CreatedAt));
            return new ConversationSummary(
                c.Id, c.Url, c.Title, c.Status, c.Error, c.CreatedAt, c.ImportedAt, c.Messages.Count,
                [new DayCoverage(day, c.Messages.Count,
                    c.Messages.Count(m => coverage.TryGetValue(day, out var keys) &&
                        keys.Contains(DailyAnalysisPolicy.MessageKey(c.Id, m))))]);
        }).ToList();

        var changedDays = summaries.SelectMany(c => c.Days.Select(d => d.Day)).Concat(reports.Select(a => a.Day))
            .Distinct().Where(day =>
            {
                var latest = reports.Where(a => a.Day == day && a.Status == "completed").MaxBy(a => a.Id);
                var batch = DailyAnalysisPolicy.SelectDay(stored, day);
                return latest == null ? batch.Count > 0 : analyses.FullPrompt(latest.Id) !=
                    AnalysisPrompt.Build(new AnalysisInput(day, latest.Prompt, batch));
            }).ToList();

        return new(summaries, reports.Select(a => new AnalysisSummary(
            a.Id, a.Status, a.Trigger, a.CreatedAt, a.CompletedAt, a.Day, a.ConversationIds,
            a.MessageKeys.Count, a.Error)).ToList(), settings.Settings(), DailySchedule.Day(clock.GetUtcNow()), TimeZoneInfo.Local.Id, changedDays, queue.DiscussionSummaries());
    }

    public Conversation? GetConversation(long id) => conversations.FindConversation(id);
    public Analysis? GetAnalysis(long id) => analyses.FindAnalysis(id);
    public List<DiscussionTurn> GetDiscussion(long id) => queue.Discussion(id);
    public long AskQuestion(long id, string question) => queue.EnqueueAnswer(id, question);
    public void RetryAnswer(long id) => queue.RetryAnswer(id);
    public Task<RefreshConversationResult> RefreshConversationAsync(long id, CancellationToken cancellationToken) =>
        refresher.RefreshAsync(id, cancellationToken);

    public async Task<RefreshDayResult> RefreshTodayAsync(CancellationToken cancellationToken)
    {
        var results = await refresher.RefreshDayAsync(DailySchedule.Day(clock.GetUtcNow()), cancellationToken);
        return new(results.Count, results.Count(r => r.Updated), results.Select(r => r.Message).ToList());
    }

    public void DeleteConversation(long id)
    {
        if (!conversations.DeleteToday(id))
            throw new ArgumentException("Smazat lze pouze dnešní konverzace.");
    }
    public string? GetFullPrompt(long id) => analyses.FullPrompt(id);

    public RuntimeSettings SaveRuntimeSettings(RuntimeSettings value)
    {
        settings.SaveRuntimeSettings(value);
        return settings.Settings().Runtime;
    }

    public AddConversationResult AddConversation(string? url)
    {
        var (conversation, added) = conversations.Add(importer.NormalizeUrl(url ?? ""));
        return new(conversation.Id, added);
    }

    public void RetryImport(long id)
    {
        if (!conversations.Retry(id))
            throw new ArgumentException("Znovu načíst lze jen neúspěšný import.");
    }

    public string SavePrompt(string? prompt, string? data)
    {
        EnsureFullPromptSize(prompt);
        var template = AnalysisPrompt.Template(prompt!, data);
        settings.SavePrompt(template);
        return template;
    }

    public PromptContent RenderPrompt(string day, string? prompt)
    {
        DailyAnalysisPolicy.ValidateDay(day);
        var template = prompt ?? settings.Settings().Prompt;
        AnalysisPrompt.Validate(template);
        var input = new AnalysisInput(day, template, DailyAnalysisPolicy.SelectDay(conversations.Conversations(), day));
        return new(AnalysisPrompt.Build(input), AnalysisPrompt.Data(input));
    }

    public long AnalyzeDay(string? day, string? prompt, string? data)
    {
        if (prompt != null)
            EnsureFullPromptSize(prompt);
        var template = prompt == null ? null : AnalysisPrompt.Template(prompt, data);
        return queue.EnqueueAnalysis("manual", day, template, data);
    }

    private static void EnsureFullPromptSize(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > AnalysisPrompt.MaximumInputCharacters)
            throw new ArgumentException("Celý prompt musí mít 1 až 240 000 znaků.");
    }
}
