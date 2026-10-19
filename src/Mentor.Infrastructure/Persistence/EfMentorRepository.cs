using Mentor.Application.Analyses;
using Mentor.Application.Configuration;
using Mentor.Application.Conversations;
using Mentor.Application.Diagnostics;
using Mentor.Application.Discussions;
using Mentor.Application.Processing;
using Mentor.Application.Prompts;
using Mentor.Infrastructure.Persistence.Analyses;
using Mentor.Infrastructure.Persistence.Configuration;
using Mentor.Infrastructure.Persistence.Conversations;
using Mentor.Infrastructure.Persistence.Discussions;
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
        foreach (var analysis in db.Analyses.Where(x => x.Status == "running").ToList())
        {
            analysis.Status = "failed";
            analysis.Error = "Rozbor byl přerušen ukončením aplikace. Spusťte nový rozbor.";
            analysis.CompletedAt = Now();
            db.Analyses.RemoveRange(db.Analyses.Where(x => x.Day == analysis.Day && x.Status == "failed"));
        }
        foreach (var turn in db.DiscussionTurns.Where(x => x.Status == "running"))
        {
            turn.Status = "failed";
            turn.Error = "Odpověď byla přerušena ukončením aplikace. Zkuste ji znovu.";
            turn.CompletedAt = Now();
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
        return new Settings(entity.Prompt, entity.LastDailyDate,
            new RuntimeSettings(entity.PollIntervalSeconds, entity.DailyTime));
    });

    public void SaveRuntimeSettings(RuntimeSettings settings)
    {
        settings.Validate();
        Write(db =>
        {
            var entity = db.Settings.Single(x => x.Id == 1);
            entity.PollIntervalSeconds = settings.PollIntervalSeconds;
            entity.DailyTime = settings.DailyTime;
            return 0;
        });
    }

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

    public bool RefreshToday(long id, ImportedConversation data) => Write(db =>
    {
        var entity = db.Conversations.SingleOrDefault(x => x.Id == id);
        if (entity == null || !IsToday(entity) || entity.Status is "queued" or "importing")
            return false;
        entity.Title = data.Title;
        entity.Messages = data.Messages;
        entity.Status = "ready";
        entity.ImportedAt = Now();
        entity.Error = null;
        return true;
    });

    public bool DeleteToday(long id) => Write(db =>
    {
        var entity = db.Conversations.SingleOrDefault(x => x.Id == id);
        if (entity == null || !IsToday(entity))
            return false;
        db.Conversations.Remove(entity);
        return true;
    });

    private bool IsToday(ConversationEntity entity) =>
        DailySchedule.Day(DateTimeOffset.Parse(entity.CreatedAt)) == DailySchedule.Day(clock.GetUtcNow());

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
            var existing = db.Analyses.OrderBy(x => x.Id).FirstOrDefault(x => x.Day == day &&
                (x.Status == "queued" || x.Status == "running"));
            if (existing != null || AnswerBusy(db, day))
            {
                if (trigger == "daily")
                {
                    if (existing != null)
                        settings.LastDailyDate = day;
                    return 0;
                }
                throw new AnalysisConflictException("Pro tento den již čeká nebo běží generování rozboru či odpovědi. Vyčkejte na dokončení.");
            }
            var batch = SelectDay(ReadConversations(db), day);
            if (batch.Count == 0)
            {
                if (trigger == "daily")
                {
                    if (!db.Conversations.Any(x => x.Status == "queued" || x.Status == "importing"))
                        settings.LastDailyDate = day;
                    return 0;
                }
                throw new ArgumentException("Pro tento den nejsou analyzovatelné zprávy.");
            }
            if (trigger == "daily")
                settings.LastDailyDate = day;
            var entity = new AnalysisEntity { Trigger = trigger, CreatedAt = Now(), Day = day, Prompt = settings.Prompt };
            if (expectedData != null)
            {
                var input = new AnalysisInput(day, settings.Prompt, batch);
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

    public AnalysisInput RefreshAnalysisInput(long id) => Write(db =>
    {
        var entity = db.Analyses.Single(x => x.Id == id && x.Status == "running");
        var input = new AnalysisInput(entity.Day, entity.Prompt, SelectDay(ReadConversations(db), entity.Day));
        Snapshot(entity, input);
        return input with
        {
            FullPrompt = entity.FullPrompt
        };
    });

    public void AnalysisDone(long id, string result) => Write(db =>
    {
        if (string.IsNullOrWhiteSpace(result))
            throw new InvalidOperationException("Generování nevrátilo platný rozbor.");
        var entity = db.Analyses.Single(x => x.Id == id && x.Status == "running");
        entity.Status = "completed";
        entity.Result = result;
        entity.Error = null;
        entity.CompletedAt = Now();
        db.Analyses.RemoveRange(db.Analyses.Where(x => x.Day == entity.Day && x.Id != id));
        return 0;
    });

    public void AnalysisFailed(long id, string error) => Write(db =>
    {
        var entity = db.Analyses.Single(x => x.Id == id && (x.Status == "queued" || x.Status == "running"));
        entity.Status = "failed";
        entity.Error = error;
        entity.CompletedAt = Now();
        db.Analyses.RemoveRange(db.Analyses.Where(x => x.Day == entity.Day && x.Id != id && x.Status == "failed"));
        return 0;
    });

    private static bool AnswerBusy(MentorDbContext db, string day) =>
        db.DiscussionTurns.Any(t => (t.Status == "queued" || t.Status == "running") &&
            db.Analyses.Any(a => a.Id == t.AnalysisId && a.Day == day));

    private static void EnsureDayFree(MentorDbContext db, string day)
    {
        if (AnswerBusy(db, day) || db.Analyses.Any(a => a.Day == day && (a.Status == "queued" || a.Status == "running")))
            throw new AnalysisConflictException("Pro tento den již čeká nebo běží generování rozboru či odpovědi. Vyčkejte na dokončení.");
    }

    public List<DiscussionTurn> Discussion(long analysisId) => Read(db => db.DiscussionTurns.AsNoTracking()
        .Where(t => t.AnalysisId == analysisId).OrderBy(t => t.Position).AsEnumerable().Select(t => t.ToModel()).ToList());

    public List<DiscussionSummary> DiscussionSummaries() => Read(db => (
        from t in db.DiscussionTurns.AsNoTracking()
        join a in db.Analyses on t.AnalysisId equals a.Id
        orderby t.Id
        select new DiscussionSummary(t.Id, t.AnalysisId, a.Day, t.Status)).ToList());

    private static string AnswerPrompt(MentorDbContext db, DiscussionTurnEntity turn)
    {
        var analysis = db.Analyses.Single(a => a.Id == turn.AnalysisId && a.Status == "completed");
        var history = db.DiscussionTurns.AsNoTracking().Where(t => t.AnalysisId == turn.AnalysisId &&
            t.Position < turn.Position && t.Status == "completed").OrderBy(t => t.Position)
            .AsEnumerable().Select(t => t.ToModel());
        return DiscussionPrompt.Build(analysis.FullPrompt!, analysis.Result!, history, turn.Question);
    }

    public long EnqueueAnswer(long analysisId, string question) => Write(db =>
    {
        if (string.IsNullOrWhiteSpace(question) || question.Length > DiscussionPrompt.MaximumInputCharacters)
            throw new ArgumentException("Otázka musí mít 1 až 240 000 znaků.");
        var analysis = db.Analyses.SingleOrDefault(a => a.Id == analysisId && a.Status == "completed")
            ?? throw new ArgumentException("Diskuse vyžaduje aktuální dokončený rozbor.");
        EnsureDayFree(db, analysis.Day);
        if (db.DiscussionTurns.Any(t => t.AnalysisId == analysisId && t.Status == "failed"))
            throw new AnalysisConflictException("Nejprve zopakujte neúspěšnou odpověď.");
        var turn = new DiscussionTurnEntity
        {
            AnalysisId = analysisId,
            Position = (db.DiscussionTurns.Where(t => t.AnalysisId == analysisId).Max(t => (int?)t.Position) ?? 0) + 1,
            Question = question,
            CreatedAt = Now()
        };
        _ = AnswerPrompt(db, turn);
        db.DiscussionTurns.Add(turn);
        db.SaveChanges();
        return turn.Id;
    });

    public void RetryAnswer(long id) => Write(db =>
    {
        var turn = db.DiscussionTurns.SingleOrDefault(t => t.Id == id && t.Status == "failed")
            ?? throw new ArgumentException("Opakovat lze pouze neúspěšnou odpověď.");
        var day = db.Analyses.Single(a => a.Id == turn.AnalysisId).Day;
        EnsureDayFree(db, day);
        _ = AnswerPrompt(db, turn);
        turn.Status = "queued";
        turn.Error = null;
        turn.CompletedAt = null;
        return 0;
    });

    public (long Id, string Prompt)? ClaimAnswer() => Write<(long, string)?>(db =>
    {
        var turn = db.DiscussionTurns.Where(t => t.Status == "queued").OrderBy(t => t.Id).FirstOrDefault();
        if (turn == null)
            return null;
        var prompt = AnswerPrompt(db, turn);
        turn.Status = "running";
        return (turn.Id, prompt);
    });

    public void AnswerDone(long id, string answer) => Write(db =>
    {
        if (string.IsNullOrWhiteSpace(answer))
            throw new InvalidOperationException("Generování nevrátilo platnou odpověď.");
        var turn = db.DiscussionTurns.Single(t => t.Id == id && t.Status == "running");
        turn.Answer = answer;
        turn.Status = "completed";
        turn.Error = null;
        turn.CompletedAt = Now();
        return 0;
    });

    public void AnswerFailed(long id, string error) => Write(db =>
    {
        var turn = db.DiscussionTurns.Single(t => t.Id == id && (t.Status == "running" || t.Status == "queued"));
        turn.Status = "failed";
        turn.Error = error;
        turn.CompletedAt = Now();
        return 0;
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
