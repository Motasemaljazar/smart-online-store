using AdminDashboard.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AdminDashboard.Pages.Admin;

[AdminAuth]
public class CommissionsModel : PageModel
{
    public void OnGet() { }
}
