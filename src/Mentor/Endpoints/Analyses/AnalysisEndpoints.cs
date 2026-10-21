using Mentor.Application.Dashboard;

namespace Mentor.Endpoints.Analyses;

public static class AnalysisEndpoints
{
    public static void MapAnalysisEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/analyses/{id:long}", (long id, MentorService service) =>
            service.GetAnalysis(id) is { } row ? Results.Ok(row) : Results.NotFound());
        api.MapGet("/analyses/{id:long}/prompt", (long id, MentorService service) =>
            service.GetAnalysis(id) == null ? Results.NotFound() : Results.Ok(new
            {
                prompt = service.GetFullPrompt(id)
            }));
        api.MapPost("/analyses", (AnalyzeDayRequest request, MentorService service) =>
            Results.Accepted(value: new
            {
                id = service.AnalyzeDay(request.Day, request.Prompt)
            }));
    }
}
