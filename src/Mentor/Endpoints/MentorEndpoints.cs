using Mentor.Application.Dashboard;
using Mentor.Application.Configuration;
using Mentor.Endpoints.Analyses;
using Mentor.Endpoints.Conversations;
using Mentor.Endpoints.Prompts;
using Mentor.Endpoints.Discussions;

namespace Mentor.Endpoints;

public static class MentorEndpoints
{
    public static IEndpointRouteBuilder MapMentorApi(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");
        api.MapGet("/state", (MentorService service) => service.GetState());
        api.MapPut("/settings", (RuntimeSettings request, MentorService service) =>
            service.SaveRuntimeSettings(request));
        api.MapGet("/conversations/{id:long}", (long id, MentorService service) =>
            service.GetConversation(id) is { } row ? Results.Ok(row) : Results.NotFound());
        api.MapPost("/conversations", (AddConversationRequest request, MentorService service) =>
            service.AddConversation(request.Url));
        api.MapPost("/conversations/refresh-today", (MentorService service, CancellationToken cancellationToken) =>
            service.RefreshTodayAsync(cancellationToken));
        api.MapPost("/conversations/{id:long}/refresh", (long id, MentorService service, CancellationToken cancellationToken) =>
            service.RefreshConversationAsync(id, cancellationToken));
        api.MapDelete("/conversations/{id:long}", (long id, MentorService service) =>
        {
            service.DeleteConversation(id);
            return Results.Ok();
        });
        api.MapPost("/conversations/{id:long}/retry", (long id, MentorService service) =>
        {
            service.RetryImport(id);
            return Results.Ok();
        });
        api.MapGet("/analyses/{id:long}", (long id, MentorService service) =>
            service.GetAnalysis(id) is { } row ? Results.Ok(row) : Results.NotFound());
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
        api.MapGet("/analyses/{id:long}/prompt", (long id, MentorService service) =>
            service.GetAnalysis(id) == null ? Results.NotFound() : Results.Ok(new
            {
                prompt = service.GetFullPrompt(id)
            }));
        api.MapPost("/analyses", (AnalyzeDayRequest request, MentorService service) =>
            Results.Accepted(value: new
            {
                id = service.AnalyzeDay(request.Day, request.Prompt, request.Data)
            }));
        api.MapPut("/prompt", (SavePromptRequest request, MentorService service) =>
            Results.Ok(new
            {
                template = service.SavePrompt(request.Prompt, request.Data)
            }));
        api.MapPost("/prompt/render", (RenderPromptRequest request, MentorService service) =>
            service.RenderPrompt(request.Day, request.Prompt));
        return endpoints;
    }
}
