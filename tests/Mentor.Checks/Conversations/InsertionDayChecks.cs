using Mentor.Application.Analyses;
using Mentor.Application.Conversations;
using Mentor.Application.Dashboard;
using Mentor.Checks.Persistence;
using Mentor.Checks.TestDoubles;

namespace Mentor.Checks.Conversations;

internal static class InsertionDayChecks
{
    public static void Run(Action<bool, string> check)
    {
        var clock = new TestClock(LocalTime.At("2026-10-17T23:59:00"));
        var path = Path.Combine(Path.GetTempPath(), "mentor-insertion-" + Guid.NewGuid(), "mentor.db");
        var store = TestRepository.Create(path, "Prompt\n{{data}}", clock);
        var service = new MentorService(store, store, store, store, new TestImporter(), clock, new ConversationRefresher(store, new TestImporter(), clock));
        const string chatGptUrl = "https://chatgpt.com/share/00000000-0000-0000-0000-000000000001";
        var first = service.AddConversation(chatGptUrl);
        store.ClaimImport();
        clock.Advance(TimeSpan.FromMinutes(2));
        store.ImportDone(first.Id, new ImportedConversation("ChatGPT", [new("user", "First", 1), new("assistant", "Reply", 2)]));
        var second = service.AddConversation("https://chatgpt.com/s/cx_00000000000000000000000000000001");
        store.ClaimImport();
        store.ImportDone(second.Id, new ImportedConversation("Codex", [new("user", "Second", 1), new("assistant", "Reply", 2)]));
        check(service.GetState().Conversations.Single(c => c.Id == first.Id).Days is [{ Day: "2026-10-17", MessageCount: 2 }] &&
            service.GetState().Conversations.Single(c => c.Id == second.Id).Days is [{ Day: "2026-10-18", MessageCount: 2 }],
            "dashboard assigns whole ChatGPT and Codex conversations to system local insertion day across midnight");
        check(DailyAnalysisPolicy.SelectDay(store.Conversations(), "2026-10-17") is [{ Id: var selected, Messages.Count: 2 }] && selected == first.Id &&
            DailyAnalysisPolicy.SelectDay(store.Conversations(), "2026-10-18").Single().Id == second.Id,
            "analysis day matches dashboard and ignores later import completion");
        check(!service.AddConversation(chatGptUrl + "?tracking=test").Added &&
            service.GetState().Conversations.Single(c => c.Id == first.Id).Days.Single().Day == "2026-10-17",
            "duplicate link keeps original insertion date");
        store = TestRepository.Create(path, "Prompt\n{{data}}", clock);
        check(DailyAnalysisPolicy.SelectDay(store.Conversations(), "2026-10-17").Single().Id == first.Id,
            "insertion day survives repository restart");
    }
}
