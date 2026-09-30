using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Pages.Account;

[Authorize]
public class ConnectStripeModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IPaymentService _paymentService;

    public ConnectStripeModel(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        IPaymentService paymentService)
    {
        _context = context;
        _currentUser = currentUser;
        _paymentService = paymentService;
    }

    public bool IsConnected { get; set; }
    public string? StripeAccountId { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var profile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (profile == null)
            return RedirectToPage("/Index");

        IsConnected = profile.StripeOnboardingComplete;
        StripeAccountId = profile.StripeAccountId;
        return Page();
    }

    public async Task<IActionResult> OnPostDashboardAsync()
    {
        var profile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (profile == null || string.IsNullOrEmpty(profile.StripeAccountId))
            return RedirectToPage();

        try
        {
            var url = await _paymentService.CreateLoginLinkAsync(profile.StripeAccountId);
            return Redirect(url);
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Could not open Stripe Dashboard: {ex.Message}";
            return RedirectToPage();
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var profile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (profile == null)
            return RedirectToPage("/Index");

        try
        {
            if (string.IsNullOrEmpty(profile.StripeAccountId))
            {
                var user = await _context.Users.FindAsync(_currentUser.UserId);
                profile.StripeAccountId = await _paymentService.CreateConnectedAccountAsync(user?.Email ?? "");
                await _context.SaveChangesAsync();
            }

            var returnUrl = $"{Request.Scheme}://{Request.Host}/Account/StripeReturn";
            var refreshUrl = $"{Request.Scheme}://{Request.Host}/Account/ConnectStripe";

            var link = await _paymentService.CreateAccountLinkAsync(
                profile.StripeAccountId,
                returnUrl,
                refreshUrl);

            return Redirect(link);
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Could not connect Stripe: {ex.Message}";
            return Page();
        }
    }
}
