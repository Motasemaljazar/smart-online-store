namespace AdminDashboard.Security;

public static class AdminAuth
{

    public static bool IsAuthenticated(HttpContext context)
        => context.User?.Identity?.IsAuthenticated == true
           && context.User.HasClaim("role", "admin");
}
