using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace AdminDashboard.Security;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class AgentAuthAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var opts = context.HttpContext.RequestServices.GetRequiredService<IOptions<AppSecurityOptions>>();

        if (!context.HttpContext.Request.Headers.TryGetValue("X-AGENT-TOKEN", out var token)
            || !AgentAuth.TryValidate(token!, opts, out var agentId)
            || agentId <= 0)
        {
            context.Result = new UnauthorizedObjectResult(new { error = "unauthorized", message = "Invalid or missing X-AGENT-TOKEN" });
            return;
        }

        context.HttpContext.Items["agentId"] = agentId;
        await next();
    }
}
