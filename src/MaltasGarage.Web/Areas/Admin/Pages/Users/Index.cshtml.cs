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

    private const int PageSize = 50;

    public List<AdminUserRow> Users { get; set; } = new();
    public string? Search { get; set; }
    public int PageNumber { get; set; } = 1;
    public int TotalPages { get; set; } = 1;

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

    public async Task OnGetAsync(string? search, int p = 1)
    {
        ViewData["ActivePage"] = "Users";
        Search = search;

        // One query, filtered and paged in the database: no lookup per profile, no loading
        // every account to search them in memory
        var now = DateTimeOffset.UtcNow;
        var query =
            from profile in _context.UserProfiles
            join user in _context.Users on profile.UserId equals user.Id
            select new { profile, user };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.user.Email!.Contains(term) || x.profile.DisplayName!.Contains(term));
        }

        var total = await query.CountAsync();
        TotalPages = Math.Max(1, (total + PageSize - 1) / PageSize);
        PageNumber = Math.Clamp(p, 1, TotalPages);

        Users = await query
            .OrderByDescending(x => x.profile.CreatedAt)
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .Select(x => new AdminUserRow
            {
                ProfileId = x.profile.Id,
                UserId = x.profile.UserId,
                DisplayName = x.profile.DisplayName,
                Email = x.user.Email,
                JoinedDate = x.profile.CreatedAt,
                TotalSales = x.profile.TotalSales,
                Rating = x.profile.Rating,
                StripeConnected = x.profile.StripeOnboardingComplete,
                IsBanned = x.user.LockoutEnd != null && x.user.LockoutEnd > now
            })
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostBanAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return RedirectToPage();

        // Staff accounts are managed on the Staff page. Here nobody bans themselves, nobody bans
        // an admin, and a manager cannot ban another manager
        if (user.Id == _userManager.GetUserId(User) ||
            await _userManager.IsInRoleAsync(user, "Admin") ||
            (await _userManager.IsInRoleAsync(user, "Manager") && !User.IsInRole("Admin")))
        {
            TempData["Error"] = "This account cannot be banned here.";
            return RedirectToPage();
        }

        await _userManager.SetLockoutEnabledAsync(user, true);
        await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        // A new security stamp makes the banned user's existing sign-in cookie invalid at the
        // next validation, instead of letting the session run on
        await _userManager.UpdateSecurityStampAsync(user);
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
