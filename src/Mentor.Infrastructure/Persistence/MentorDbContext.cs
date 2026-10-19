using System.Data.Common;
using System.Text.Json;
using Mentor.Infrastructure.Persistence.Analyses;
using Mentor.Infrastructure.Persistence.Configuration;
using Mentor.Infrastructure.Persistence.Conversations;
using Mentor.Infrastructure.Persistence.Discussions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mentor.Infrastructure.Persistence;

public sealed class MentorDbContext(DbContextOptions<MentorDbContext> options) : DbContext(options)
{
    internal DbSet<ConversationEntity> Conversations => Set<ConversationEntity>();
    internal DbSet<AnalysisEntity> Analyses => Set<AnalysisEntity>();
    internal DbSet<SettingsEntity> Settings => Set<SettingsEntity>();
    internal DbSet<DiscussionTurnEntity> DiscussionTurns => Set<DiscussionTurnEntity>();

    public static string ConnectionString(string path) => new DbConnectionStringBuilder
    {
        ["Data Source"] = Path.GetFullPath(path),
        ["Default Timeout"] = 5
    }.ConnectionString;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var conversations = modelBuilder.Entity<ConversationEntity>();
        conversations.ToTable("conversations");
        conversations.HasKey(x => x.Id);
        conversations.HasIndex(x => x.Url).IsUnique();
        conversations.Property(x => x.Id).HasColumnName("id");
        conversations.Property(x => x.Url).HasColumnName("url");
        conversations.Property(x => x.Title).HasColumnName("title");
        conversations.Property(x => x.Status).HasColumnName("status");
        conversations.Property(x => x.Error).HasColumnName("error");
        conversations.Property(x => x.CreatedAt).HasColumnName("created_at");
        conversations.Property(x => x.ImportedAt).HasColumnName("imported_at");
        JsonList(conversations.Property(x => x.Messages).HasColumnName("messages"));

        var analyses = modelBuilder.Entity<AnalysisEntity>();
        analyses.ToTable("analyses");
        analyses.HasKey(x => x.Id);
        analyses.Property(x => x.Id).HasColumnName("id");
        analyses.Property(x => x.Status).HasColumnName("status");
        analyses.Property(x => x.Trigger).HasColumnName("trigger");
        analyses.Property(x => x.CreatedAt).HasColumnName("created_at");
        analyses.Property(x => x.CompletedAt).HasColumnName("completed_at");
        analyses.Property(x => x.Day).HasColumnName("analysis_day");
        analyses.Property(x => x.Prompt).HasColumnName("prompt");
        analyses.Property(x => x.FullPrompt).HasColumnName("full_prompt");
        analyses.Property(x => x.Result).HasColumnName("result");
        analyses.Property(x => x.Error).HasColumnName("error");
        JsonList(analyses.Property(x => x.ConversationIds).HasColumnName("conversation_ids"));
        JsonList(analyses.Property(x => x.MessageKeys).HasColumnName("message_keys"));

        var turns = modelBuilder.Entity<DiscussionTurnEntity>();
        turns.ToTable("discussion_turns");
        turns.HasKey(x => x.Id);
        turns.HasIndex(x => new { x.AnalysisId, x.Position }).IsUnique();
        turns.HasOne<AnalysisEntity>().WithMany().HasForeignKey(x => x.AnalysisId).OnDelete(DeleteBehavior.Cascade);

        var settings = modelBuilder.Entity<SettingsEntity>();
        settings.ToTable("settings");
        settings.HasKey(x => x.Id);
        settings.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        settings.Property(x => x.Prompt).HasColumnName("prompt");
        settings.Property(x => x.LastDailyDate).HasColumnName("last_daily_date");
        settings.Property(x => x.PollIntervalSeconds).HasColumnName("poll_interval_seconds");
        settings.Property(x => x.DailyTime).HasColumnName("daily_time");
    }

    private static void JsonList<T>(PropertyBuilder<List<T>> property)
    {
        property.HasConversion(
            value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
            value => JsonSerializer.Deserialize<List<T>>(value, (JsonSerializerOptions?)null)!);
        property.Metadata.SetValueComparer(new ValueComparer<List<T>>(
            (left, right) => left != null && right != null && left.SequenceEqual(right),
            value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
            value => value.ToList()));
    }
}
