using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Areas.Admin.Pages.Users;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly IEmailService _email;

    public IndexModel(ApplicationDbContext context, UserManager<IdentityUser> userManager, IEmailService email)
    {
        _context = context;
        _userManager = userManager;
        _email = email;
    }

    public List<AdminUserRow> Users { get; set; } = new();
    public string? Search { get; set; }

    public class AdminUserRow
    {
        public Guid ProfileId { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? Email { get; set; }
        public DateTime JoinedDate { get; set; }
        public int TotalSales { get; set; }
        public decimal Rating { get; set; }
        public bool StripeConnected { get; set; }
        public bool IsBanned { get; set; }
    }

    public async Task OnGetAsync(string? search)
    {
        ViewData["ActivePage"] = "Users";
        Search = search;

        var profiles = await _context.UserProfiles
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        var rows = new List<AdminUserRow>();
        foreach (var profile in profiles)
        {
            var identityUser = await _userManager.FindByIdAsync(profile.UserId);
            if (identityUser == null) continue;

            var email = identityUser.Email ?? string.Empty;
            if (!string.IsNullOrEmpty(search) &&
                !email.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                !(profile.DisplayName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
                continue;

            var isBanned = identityUser.LockoutEnd.HasValue &&
                           identityUser.LockoutEnd.Value > DateTimeOffset.UtcNow;

            rows.Add(new AdminUserRow
            {
                ProfileId = profile.Id,
                UserId = profile.UserId,
                DisplayName = profile.DisplayName,
                Email = email,
                JoinedDate = profile.CreatedAt,
                TotalSales = profile.TotalSales,
                Rating = profile.Rating,
                StripeConnected = profile.StripeOnboardingComplete,
                IsBanned = isBanned
            });
        }

        Users = rows;
    }

    public async Task<IActionResult> OnPostBanAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user != null)
        {
            await _userManager.SetLockoutEnabledAsync(user, true);
            await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUnbanAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user != null)
        {
            await _userManager.SetLockoutEndDateAsync(user, null);
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSendEmailAsync(string userId, string subject, string message)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user?.Email == null)
            return RedirectToPage();

        var profile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId);
        var displayName = profile?.DisplayName ?? user.Email;

        var html = $"<p>Hi {System.Web.HttpUtility.HtmlEncode(displayName)},</p>" +
                   $"<p>{System.Web.HttpUtility.HtmlEncode(message).Replace("\n", "<br>")}</p>" +
                   $"<p style='color:#6c757d;font-size:0.875rem;margin-top:2rem;'>Malta's Garage Support</p>";

        await _email.SendAsync(user.Email, displayName, subject, html);

        TempData["EmailSent"] = $"Email sent to {user.Email}";
        return RedirectToPage();
    }
}
