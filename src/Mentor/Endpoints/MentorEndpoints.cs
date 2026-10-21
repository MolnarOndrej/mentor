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
        api.MapConversationEndpoints();
        api.MapAnalysisEndpoints();
        api.MapDiscussionEndpoints();
        api.MapPromptEndpoints();
        return endpoints;
    }
}
