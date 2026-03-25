using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AdminDashboard.Security;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class CustomerAuthAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.HttpContext.Request.Headers.TryGetValue("X-CUSTOMER-ID", out var customerIdHeader)
            || !int.TryParse(customerIdHeader, out var customerId)
            || customerId <= 0)
        {
            context.Result = new UnauthorizedObjectResult(new { error = "unauthorized", message = "X-CUSTOMER-ID header is required" });
            return;
        }

        context.HttpContext.Items["customerId"] = customerId;
        await next();
    }
}
