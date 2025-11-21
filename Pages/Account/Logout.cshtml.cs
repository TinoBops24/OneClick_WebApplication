using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Http;

namespace OneClick_WebApp.Pages.Account
{
    public class LogoutModel : PageModel
    {
        public IActionResult OnGet()
        {
            // Clear all session data
            HttpContext.Session.Clear();

            // Also clear any legacy token cookies if they exist
            if (Request.Cookies.ContainsKey("firebaseToken"))
            {
                Response.Cookies.Delete("firebaseToken");
            }

            // Redirect to homepage
            return RedirectToPage("/Index");
        }
    }
}