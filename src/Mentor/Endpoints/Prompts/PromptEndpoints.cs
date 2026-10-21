using Mentor.Application.Dashboard;

namespace Mentor.Endpoints.Prompts;

public static class PromptEndpoints
{
    public static void MapPromptEndpoints(this RouteGroupBuilder api)
    {
        api.MapPut("/prompt", (SavePromptRequest request, MentorService service) =>
            Results.Ok(new
            {
                template = service.SavePrompt(request.Prompt)
            }));
    }
}
