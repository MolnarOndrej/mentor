using Mentor.Application.Dashboard;

namespace Mentor.Endpoints.Discussions;

public static class DiscussionEndpoints
{
    public static void MapDiscussionEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/analyses/{id:long}/discussion", (long id, MentorService service) =>
            service.GetAnalysis(id) is { Status: "completed" } ? Results.Ok(service.GetDiscussion(id)) : Results.NotFound());
        api.MapPost("/analyses/{id:long}/discussion", (long id, AskQuestionRequest request, MentorService service) =>
            Results.Accepted(value: new
            {
                id = service.AskQuestion(id, request.Question)
            }));
        api.MapPost("/discussion/{id:long}/retry", (long id, MentorService service) =>
        {
            service.RetryAnswer(id);
            return Results.Accepted();
        });
    }
}
