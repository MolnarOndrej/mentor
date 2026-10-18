using Mentor.Application.Analyses;

namespace Mentor.Application.Conversations;

public sealed class ConversationRefresher(IConversationRepository conversations, IConversationImporter importer, TimeProvider clock)
{
    public async Task<RefreshConversationResult> RefreshAsync(long id, CancellationToken cancellationToken)
    {
        var conversation = conversations.FindConversation(id);
        if (conversation == null || DailySchedule.Day(DateTimeOffset.Parse(conversation.CreatedAt)) != DailySchedule.Day(clock.GetUtcNow()))
            return new(false, "Upravovat lze pouze dostupné dnešní konverzace.");
        if (conversation.Status is "queued" or "importing")
            return new(false, "Konverzace se právě načítá.");

        ImportedConversation data;
        try
        {
            data = await importer.ImportAsync(conversation.Url, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return new(false, "Odkaz není platný nebo dostupný. Ponechána poslední uložená data.");
        }
        return conversations.RefreshToday(id, data)
            ? new(true, "Konverzace byla aktualizována.")
            : new(false, "Konverzace již není dostupná pro aktualizaci.");
    }

    public async Task RefreshDayAsync(string day, CancellationToken cancellationToken)
    {
        if (day != DailySchedule.Day(clock.GetUtcNow()))
            return;
        foreach (var conversation in conversations.Conversations().Where(c =>
            DailySchedule.Day(DateTimeOffset.Parse(c.CreatedAt)) == day))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // A conversation may have been deleted while the preceding link was loading.
            if (conversations.FindConversation(conversation.Id) != null)
                await RefreshAsync(conversation.Id, cancellationToken);
        }
    }
}
