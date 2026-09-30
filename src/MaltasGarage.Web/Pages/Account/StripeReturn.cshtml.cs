using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Pages.Account;

[Authorize]
public class StripeReturnModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IPaymentService _paymentService;

    public StripeReturnModel(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        IPaymentService paymentService)
    {
        _context = context;
        _currentUser = currentUser;
        _paymentService = paymentService;
    }

    public bool IsReady { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var profile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (profile == null || string.IsNullOrEmpty(profile.StripeAccountId))
            return RedirectToPage("/Account/ConnectStripe");

        IsReady = await _paymentService.IsAccountReadyAsync(profile.StripeAccountId);

        if (IsReady)
        {
            profile.StripeOnboardingComplete = true;
            await _context.SaveChangesAsync();
        }

        return Page();
    }
}
