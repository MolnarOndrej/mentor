using Mentor.Application.Conversations;
using Mentor.Application.Analyses;
using Mentor.Application.Configuration;
using Mentor.Application.Dashboard;
using Mentor.Application.Diagnostics;
using Mentor.Application.Processing;
using Mentor.Application.Prompts;
using Mentor.Checks.Persistence;
using Mentor.Checks.TestDoubles;

namespace Mentor.Checks.Application;

internal static class RuntimeSettingsChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var clock = new TestClock(LocalTime.At("2026-07-01T20:14:59"));
        var path = Path.Combine(Path.GetTempPath(), "mentor-settings-" + Guid.NewGuid(), "mentor.db");
        var repository = TestRepository.Create(path, "Instructions\n{{data}}", clock);
        var service = new MentorService(repository, repository, repository, repository, new TestImporter(), clock, new ConversationRefresher(repository, new TestImporter(), clock));
        check(service.GetState().Settings.Runtime == new RuntimeSettings(5, "22:00"),
            "new database exposes default polling and analysis schedule");
        check(service.GetState().TimeZone == TimeZoneInfo.Local.Id &&
            System.Text.Json.JsonDocument.Parse(AnalysisPrompt.Data(new("2026-07-01", "{{data}}", [])))
                .RootElement.GetProperty("TimeZone").GetString() == TimeZoneInfo.Local.Id,
            "dashboard and analysis prompt report the system time zone");

        foreach (var invalid in new[] { new RuntimeSettings(0), new RuntimeSettings(3601),
            new RuntimeSettings(3, "24:00"), new RuntimeSettings(3, "9:00"),
            new RuntimeSettings(3, "12:60"), new RuntimeSettings(3, null!) })
        {
            try
            {
                service.SaveRuntimeSettings(invalid);
                throw new InvalidOperationException("Invalid settings accepted.");
            }
            catch (ArgumentException) { }
        }
        check(repository.Settings().Runtime == new RuntimeSettings(), "invalid settings never overwrite persisted values");
        check(service.SaveRuntimeSettings(new(3, "20:15")) == new RuntimeSettings(3, "20:15"),
            "settings service returns the saved database values");
        repository.SavePrompt("Edited instructions\n{{data}}");
        repository = TestRepository.Create(path, "Ignored prompt\n{{data}}", clock);
        check(repository.Settings() is
        {
            Runtime: { PollIntervalSeconds: 3, DailyTime: "20:15" },
            Prompt: "Edited instructions\n{{data}}", LastDailyDate: null
        },
            "runtime settings and prompt survive repository restart independently");

        using var telemetry = new MentorTelemetry();
        var processor = new WorkProcessor(repository, new TestImporter(), new TestAnalyzer(),
            clock, telemetry, new TestLogger(), new ConversationRefresher(repository, new TestImporter(), clock));
        check(!await processor.ProcessNextAsync(repository.Settings(), default), "worker waits until configured analysis minute");
        clock.Advance(TimeSpan.FromSeconds(1));
        check(!await processor.ProcessNextAsync(repository.Settings(), default) && repository.Settings().LastDailyDate == "2026-07-01",
            "worker checks configured time and skips an empty daily analysis");
        repository.SaveRuntimeSettings(new(1, "20:16"));
        clock.Advance(TimeSpan.FromMinutes(1));
        check(!await processor.ProcessNextAsync(repository.Settings(), default) && repository.Analyses().Count == 0,
            "changing schedule does not repeat an already completed daily analysis");
        clock.Advance(TimeSpan.FromDays(1));
        repository.SaveRuntimeSettings(new(1, "20:17"));
        check(!await processor.ProcessNextAsync(repository.Settings(), default), "worker uses updated schedule without restart");
        clock.Advance(TimeSpan.FromMinutes(1));
        check(!await processor.ProcessNextAsync(repository.Settings(), default) && repository.Settings().LastDailyDate == "2026-07-02", "updated analysis time takes effect the next day");
        check(DailySchedule.DueDate(LocalTime.At("2026-12-01T20:14:59"), null, "20:15") == null &&
            DailySchedule.DueDate(LocalTime.At("2026-12-01T20:15:00"), null, "20:15") == "2026-12-01" &&
            DailySchedule.DueDate(LocalTime.At("2026-12-02T00:00:00"), null, "00:00") == "2026-12-02",
            "custom schedule respects system local winter time, minute boundaries and midnight");
    }
}
