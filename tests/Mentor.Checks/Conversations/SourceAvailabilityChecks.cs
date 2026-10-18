using System.Net;
using System.Text;
using System.Text.Json;
using Mentor.Application.Conversations;
using Mentor.Checks.Persistence;
using Mentor.Checks.TestDoubles;
using Mentor.Infrastructure.Configuration;
using Mentor.Infrastructure.Conversations;
using Microsoft.Extensions.Options;

namespace Mentor.Checks.Conversations;

internal static class SourceAvailabilityChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var clock = new TestClock(LocalTime.At("2026-10-18T12:00:00"));
        foreach (var url in new[] { "https://chatgpt.com/s/cx_00000000000000000000000000000001",
            "https://chatgpt.com/share/00000000-0000-0000-0000-000000000001" })
        {
            var path = Path.Combine(Path.GetTempPath(), "mentor-source-" + Guid.NewGuid(), "mentor.db");
            var repository = TestRepository.Create(path, "{{data}}", clock);
            var conversation = repository.Add(url).Conversation;
            repository.ClaimImport();
            repository.ImportDone(conversation.Id, new("Saved", [new("user", "Saved content", 1)]));
            var before = JsonSerializer.Serialize(repository.FindConversation(conversation.Id));
            foreach (var status in new[] { HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.Found })
            {
                var snapshotRequested = false;
                using var handler = new ImportHttpHandler(request =>
                {
                    if (request.RequestUri!.AbsoluteUri == url)
                        return new(status)
                        {
                            Content = new StringContent("Unavailable", Encoding.UTF8, "text/html")
                        };
                    snapshotRequested = true;
                    return new(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                        """{"version":1,"title":"Stale snapshot","turns":[{"items":[{"type":"userMessage","content":[{"type":"text","text":"Stale content"}]}]}]}""",
                        Encoding.UTF8, "application/json")
                    };
                });
                using var http = new HttpClient(handler);
                var importer = new ChatGptImporter(http, Options.Create(new MentorInfrastructureOptions()));
                var refresher = new ConversationRefresher(repository, importer, clock);
                var result = await refresher.RefreshAsync(conversation.Id, default);
                check(!result.Updated && result.Message.Contains($"HTTP {(int)status}") && !snapshotRequested &&
                    before == JsonSerializer.Serialize(repository.FindConversation(conversation.Id)),
                    $"inaccessible public link ({(int)status}) reports HTTP error and preserves saved data without using stale snapshot");
            }
        }
    }
}
