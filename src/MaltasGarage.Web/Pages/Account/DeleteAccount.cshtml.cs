using System.ComponentModel.DataAnnotations;
using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Pages.Account;

[Authorize]
public class DeleteAccountModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly SignInManager<IdentityUser> _signInManager;

    public DeleteAccountModel(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager)
    {
        _context = context;
        _currentUser = currentUser;
        _userManager = userManager;
        _signInManager = signInManager;
    }

    [BindProperty]
    [Required(ErrorMessage = "Please enter your password to confirm.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public List<string> Blockers { get; set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadBlockersAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return RedirectToPage("/Index");

        // Verify password
        if (!await _userManager.CheckPasswordAsync(user, Password))
        {
            ModelState.AddModelError(nameof(Password), "Incorrect password.");
            await LoadBlockersAsync();
            return Page();
        }

        await LoadBlockersAsync();
        if (Blockers.Any())
            return Page();

        var profile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (profile == null) return RedirectToPage("/Index");

        // 1. Cancel all listings with no bids
        var listings = await _context.Listings
            .Include(l => l.Bids)
            .Where(l => l.SellerId == profile.Id &&
                        l.Status != ListingStatus.Sold &&
                        l.Status != ListingStatus.Cancelled)
            .ToListAsync();

        foreach (var listing in listings)
        {
            listing.Status = ListingStatus.Cancelled;
            listing.UpdatedAt = DateTime.UtcNow;
        }

        // 2. Delete favourites
        var favourites = await _context.Favourites
            .Where(f => f.UserId == profile.Id)
            .ToListAsync();
        _context.Favourites.RemoveRange(favourites);

        // 3. Delete notification preferences
        var prefs = await _context.NotificationPreferences
            .Where(p => p.UserProfileId == profile.Id)
            .ToListAsync();
        _context.NotificationPreferences.RemoveRange(prefs);

        // 4. Delete system messages
        var systemMessages = await _context.SystemMessages
            .Where(m => m.UserId == profile.Id)
            .ToListAsync();
        _context.SystemMessages.RemoveRange(systemMessages);

        // 5. Delete conversations and their chat messages
        var conversations = await _context.Conversations
            .Include(c => c.Messages)
            .Where(c => c.BuyerId == profile.Id || c.SellerId == profile.Id)
            .ToListAsync();

        foreach (var conv in conversations)
            _context.ChatMessages.RemoveRange(conv.Messages);
        _context.Conversations.RemoveRange(conversations);

        // 6. Delete dispute attachments on disputes opened by user
        var disputeAttachments = await _context.DisputeAttachments
            .Where(a => a.Dispute.Order.BuyerId == profile.Id)
            .ToListAsync();
        _context.DisputeAttachments.RemoveRange(disputeAttachments);

        // 7. Anonymize the UserProfile - keep it for order referential integrity
        profile.DisplayName       = "Deleted User";
        profile.AvatarUrl         = null;
        profile.Bio               = null;
        profile.Location          = null;
        profile.PhoneNumber       = null;
        profile.StripeAccountId   = null;
        profile.StripeOnboardingComplete = false;
        // Detach from IdentityUser so the UserId index is freed
        profile.UserId            = $"deleted_{profile.Id}";
        profile.UpdatedAt         = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // 8. Delete the IdentityUser (removes email / password / login)
        await _signInManager.SignOutAsync();
        await _userManager.DeleteAsync(user);

        return RedirectToPage("/Index");
    }

    private async Task LoadBlockersAsync()
    {
        Blockers.Clear();

        var profile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (profile == null) return;

        var activeOrderStatuses = new[] { OrderStatus.Paid, OrderStatus.Shipped, OrderStatus.Disputed };

        var activeOrderCount = await _context.Orders
            .CountAsync(o => (o.BuyerId == profile.Id || o.SellerId == profile.Id)
                          && activeOrderStatuses.Contains(o.Status));

        if (activeOrderCount > 0)
            Blockers.Add($"You have {activeOrderCount} active order(s) (Paid/Shipped/Disputed). Please complete or resolve them before deleting your account.");

        var auctionsWithBids = await _context.Listings
            .CountAsync(l => l.SellerId == profile.Id
                          && l.Status == ListingStatus.AuctionPhase
                          && l.Bids.Any());

        if (auctionsWithBids > 0)
            Blockers.Add($"You have {auctionsWithBids} active auction(s) with bids. Please wait for them to complete.");
    }
}
