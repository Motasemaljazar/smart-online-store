using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace AdminDashboard.Security;

public class AdminApiKeyAuthorizationHandler : AuthorizationHandler<AdminOnlyRequirement>
{
    private readonly AppSecurityOptions _opts;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AdminApiKeyAuthorizationHandler(IOptions<AppSecurityOptions> opts, IHttpContextAccessor httpContextAccessor)
    {
        _opts = opts.Value;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AdminOnlyRequirement requirement)
    {
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.Request.Headers.TryGetValue("X-ADMIN-KEY", out var key) == true
            && string.Equals(key, _opts.AdminApiKey, StringComparison.Ordinal))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public class AdminOnlyRequirement : IAuthorizationRequirement { }
