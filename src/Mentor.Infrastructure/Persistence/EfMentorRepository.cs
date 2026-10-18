using Mentor.Application.Analyses;
using Mentor.Application.Configuration;
using Mentor.Application.Conversations;
using Mentor.Application.Diagnostics;
using Mentor.Application.Processing;
using Mentor.Application.Prompts;
using Mentor.Infrastructure.Persistence.Analyses;
using Mentor.Infrastructure.Persistence.Configuration;
using Mentor.Infrastructure.Persistence.Conversations;
using Microsoft.EntityFrameworkCore;
using static Mentor.Application.Analyses.DailyAnalysisPolicy;

namespace Mentor.Infrastructure.Persistence;

// WorkspaceLease enforces a single process. The shared gate serializes queue claims
// with HTTP operations; each operation owns a fresh context and its transaction.
public sealed class EfMentorRepository : IConversationRepository, IAnalysisRepository,
    ISettingsRepository, IWorkQueue, IRepositoryDiagnostics
{
    private readonly object gate = new();
    private readonly IDbContextFactory<MentorDbContext> contexts;
    private readonly TimeProvider clock;

    public EfMentorRepository(IDbContextFactory<MentorDbContext> contexts, string initialPrompt, TimeProvider clock)
    {
        this.contexts = contexts;
        this.clock = clock;
        using var db = contexts.CreateDbContext();
        db.Database.EnsureCreated();
        using var transaction = db.Database.BeginTransaction();
        foreach (var conversation in db.Conversations.Where(x => x.Status == "importing"))
        {
            conversation.Status = "queued";
            conversation.Error = null;
        }
        foreach (var analysis in db.Analyses.Where(x => x.Status == "running"))
        {
            analysis.Status = "failed";
            analysis.Error = "Aplikace byla během rozboru ukončena. Spusťte nový rozbor.";
            analysis.CompletedAt = Now();
        }
        if (!db.Settings.Any(x => x.Id == 1))
            db.Settings.Add(new SettingsEntity { Id = 1, Prompt = initialPrompt });
        db.SaveChanges();
        transaction.Commit();
    }

    private string Now() => clock.GetUtcNow().ToString("O");
    private T Read<T>(Func<MentorDbContext, T> read)
    {
        lock (gate)
        {
            using var db = contexts.CreateDbContext();
            return read(db);
        }
    }

    private T Write<T>(Func<MentorDbContext, T> change)
    {
        lock (gate)
        {
            using var db = contexts.CreateDbContext();
            using var transaction = db.Database.BeginTransaction();
            var result = change(db);
            db.SaveChanges();
            transaction.Commit();
            return result;
        }
    }

    private void UpdateConversation(long id, Action<ConversationEntity> change) => Write(db =>
    {
        var entity = db.Conversations.SingleOrDefault(x => x.Id == id);
        if (entity != null)
            change(entity);
        return 0;
    });

    private void UpdateAnalysis(long id, Action<AnalysisEntity> change) => Write(db =>
    {
        var entity = db.Analyses.SingleOrDefault(x => x.Id == id);
        if (entity != null)
            change(entity);
        return 0;
    });

    private static List<Conversation> ReadConversations(MentorDbContext db) => db.Conversations
        .AsNoTracking().OrderByDescending(x => x.Id).AsEnumerable().Select(x => x.ToModel()).ToList();

    public List<Conversation> Conversations() => Read(ReadConversations);
    public Conversation? FindConversation(long id) => Read(db => db.Conversations.AsNoTracking()
        .SingleOrDefault(x => x.Id == id)?.ToModel());
    private static IQueryable<Analysis> AnalysisModels(IQueryable<AnalysisEntity> query) => query.AsNoTracking()
        .Select(x => new Analysis(x.Id, x.Status, x.Trigger, x.CreatedAt, x.CompletedAt, x.Day, x.Prompt,
            x.ConversationIds, x.MessageKeys, x.Result, x.Error));

    public List<Analysis> Analyses() => Read(db => AnalysisModels(db.Analyses
        .OrderByDescending(x => x.Day).ThenByDescending(x => x.Id)).ToList());
    public Analysis? FindAnalysis(long id) => Read(db => AnalysisModels(db.Analyses.Where(x => x.Id == id)).SingleOrDefault());
    public string? FullPrompt(long id) => Read(db => db.Analyses.Where(x => x.Id == id)
        .Select(x => x.FullPrompt).SingleOrDefault());
    public Settings Settings() => Read(db =>
    {
        var entity = db.Settings.AsNoTracking().Single(x => x.Id == 1);
        return new Settings(entity.Prompt, entity.LastDailyDate);
    });

    public void SavePrompt(string prompt)
    {
        AnalysisPrompt.Validate(prompt);
        Write(db => db.Settings.Single(x => x.Id == 1).Prompt = prompt);
    }

    public (Conversation Conversation, bool Added) Add(string url) => Write(db =>
    {
        var existing = db.Conversations.SingleOrDefault(x => x.Url == url);
        if (existing != null)
            return (existing.ToModel(), false);
        var entity = new ConversationEntity { Url = url, Title = "Čeká na načtení", CreatedAt = Now() };
        db.Conversations.Add(entity);
        db.SaveChanges();
        return (entity.ToModel(), true);
    });

    public bool Retry(long id) => Write(db =>
    {
        var entity = db.Conversations.SingleOrDefault(x => x.Id == id && x.Status == "failed");
        if (entity == null)
            return false;
        entity.Status = "queued";
        entity.Error = null;
        return true;
    });

    public Conversation? ClaimImport() => Write(db =>
    {
        var entity = db.Conversations.Where(x => x.Status == "queued").OrderBy(x => x.Id).FirstOrDefault();
        if (entity == null)
            return null;
        entity.Status = "importing";
        return entity.ToModel();
    });

    public void ImportDone(long id, ImportedConversation data) => UpdateConversation(id, entity =>
    {
        entity.Title = data.Title;
        entity.Messages = data.Messages;
        entity.Status = "ready";
        entity.ImportedAt = Now();
        entity.Error = null;
    });

    public void ImportFailed(long id, string error) => UpdateConversation(id, entity =>
    {
        entity.Status = "failed";
        entity.Error = error;
    });

    public long EnqueueAnalysis(string trigger, string? day = null, string? promptOverride = null, string? expectedData = null)
    {
        day ??= DailySchedule.Day(clock.GetUtcNow());
        ValidateDay(day);
        if (promptOverride != null)
            AnalysisPrompt.Validate(promptOverride);
        return Write(db =>
        {
            var settings = db.Settings.Single(x => x.Id == 1);
            if (trigger == "daily" && settings.LastDailyDate == day)
                return 0;
            if (promptOverride != null)
                settings.Prompt = promptOverride;
            if (trigger == "daily")
                settings.LastDailyDate = day;
            var existing = db.Analyses.OrderBy(x => x.Id).FirstOrDefault(x => x.Day == day &&
                (x.Status == "queued" || x.Status == "running"));
            if (existing != null)
            {
                if (expectedData != null)
                {
                    var requested = new AnalysisInput(day, promptOverride ?? existing.Prompt,
                        SelectDay(ReadConversations(db), day));
                    if (existing.FullPrompt != AnalysisPrompt.Build(requested))
                        throw new AnalysisConflictException("Rozbor tohoto dne již čeká nebo běží s jiným zadáním. Vyčkejte na jeho dokončení.");
                }
                return existing.Id;
            }
            var entity = new AnalysisEntity { Trigger = trigger, CreatedAt = Now(), Day = day, Prompt = settings.Prompt };
            if (expectedData != null)
            {
                var conversations = ReadConversations(db);
                var input = new AnalysisInput(day, settings.Prompt, SelectDay(conversations, day));
                if (conversations.Any(x => x.Status is "queued" or "importing") || AnalysisPrompt.Data(input) != expectedData)
                    throw new AnalysisConflictException("Konverzace se změnily nebo ještě probíhá import. Otevřete zadání znovu a zkontrolujte aktuální prompt.");
                Snapshot(entity, input);
            }
            db.Analyses.Add(entity);
            db.SaveChanges();
            return entity.Id;
        });
    }

    public (long Id, AnalysisInput Input)? ClaimAnalysis() => Write<(long, AnalysisInput)?>(db =>
    {
        if (db.Conversations.Any(x => x.Status == "queued" || x.Status == "importing"))
            return null;
        var entity = db.Analyses.Where(x => x.Status == "queued").OrderBy(x => x.Id).FirstOrDefault();
        if (entity == null)
            return null;
        var conversations = ReadConversations(db);
        var batch = entity.FullPrompt == null ? SelectDay(conversations, entity.Day)
            : RestoreSnapshot(conversations, entity.MessageKeys.ToHashSet());
        var input = new AnalysisInput(entity.Day, entity.Prompt, batch, entity.FullPrompt);
        if (entity.FullPrompt == null)
            Snapshot(entity, input);
        entity.Status = "running";
        return (entity.Id, input);
    });

    private static void Snapshot(AnalysisEntity entity, AnalysisInput input)
    {
        entity.ConversationIds = input.Conversations.Select(x => x.Id).ToList();
        entity.MessageKeys = input.Conversations.SelectMany(c => c.Messages.Select(m => MessageKey(c.Id, m))).ToList();
        entity.FullPrompt = AnalysisPrompt.Build(input);
    }

    public void AnalysisDone(long id, string result) => UpdateAnalysis(id, entity =>
    {
        entity.Status = "completed";
        entity.Result = result;
        entity.CompletedAt = Now();
    });

    public void AnalysisFailed(long id, string error) => UpdateAnalysis(id, entity =>
    {
        entity.Status = "failed";
        entity.Error = error;
        entity.CompletedAt = Now();
    });

    public void Probe() => Read(db =>
    {
        _ = db.Settings.Single(x => x.Id == 1);
        _ = db.Conversations.Any();
        _ = db.Analyses.Any();
        return 0;
    });

    public QueueStatistics GetQueueStatistics() => Read(db =>
    {
        var imports = db.Conversations.GroupBy(x => x.Status).Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionary(x => x.Status, x => x.Count);
        var analyses = db.Analyses.GroupBy(x => x.Status).Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionary(x => x.Status, x => x.Count);
        return new QueueStatistics(imports.GetValueOrDefault("queued"), analyses.GetValueOrDefault("queued"),
            imports.GetValueOrDefault("importing"), analyses.GetValueOrDefault("running"),
            imports.GetValueOrDefault("failed"), analyses.GetValueOrDefault("failed"));
    });
}
