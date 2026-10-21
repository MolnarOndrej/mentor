using Mentor.Application.Dashboard;

namespace Mentor.Endpoints.Conversations;

public static class ConversationEndpoints
{
    public static void MapConversationEndpoints(this RouteGroupBuilder api)
    {
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
    }
}
