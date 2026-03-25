using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AdminDashboard.Pages;

public class LoginModel : PageModel
{
    public IActionResult OnGet()
        => Redirect("/Admin/Login");

    public IActionResult OnPost()
        => Redirect("/Admin/Login");
}
