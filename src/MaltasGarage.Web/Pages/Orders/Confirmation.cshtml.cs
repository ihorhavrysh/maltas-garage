using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MaltasGarage.Web.Pages.Orders;

[Authorize]
public class ConfirmationModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid OrderId { get; set; }

    public void OnGet()
    {
    }
}
