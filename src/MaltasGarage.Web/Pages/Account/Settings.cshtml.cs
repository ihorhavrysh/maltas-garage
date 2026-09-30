using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Pages.Account;

[Authorize]
public class SettingsModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly UserManager<IdentityUser> _userManager;

    public SettingsModel(ApplicationDbContext context, ICurrentUserService currentUser, UserManager<IdentityUser> userManager)
    {
        _context = context;
        _currentUser = currentUser;
        _userManager = userManager;
    }

    public UserProfile? Profile { get; set; }
    public string? CurrentEmail { get; set; }

    [BindProperty]
    public NotificationInput Notifications { get; set; } = new();

    public class NotificationInput
    {
        public bool NewBidOnListing { get; set; }
        public bool BidOutbid { get; set; }
        public bool AuctionWon { get; set; }
        public bool NewOrder { get; set; }
        public bool OrderShipped { get; set; }
        public bool OrderCompleted { get; set; }
        public bool NewChatMessage { get; set; }
        public bool DisputeUpdate { get; set; }
        public bool OfferUpdate { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        Profile = await _context.UserProfiles
            .Include(p => p.NotificationPreferences)
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (Profile == null) return NotFound();

        var identityUser = await _userManager.FindByIdAsync(_currentUser.UserId!);
        CurrentEmail = identityUser?.Email;

        var prefs = Profile.NotificationPreferences;
        if (prefs != null)
        {
            Notifications = new NotificationInput
            {
                NewBidOnListing = prefs.NewBidOnListing,
                BidOutbid = prefs.BidOutbid,
                AuctionWon = prefs.AuctionWon,
                NewOrder = prefs.NewOrder,
                OrderShipped = prefs.OrderShipped,
                OrderCompleted = prefs.OrderCompleted,
                NewChatMessage = prefs.NewChatMessage,
                DisputeUpdate = prefs.DisputeUpdate,
                OfferUpdate = prefs.OfferUpdate
            };
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Profile = await _context.UserProfiles
            .Include(p => p.NotificationPreferences)
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (Profile == null) return NotFound();

        var identityUser = await _userManager.FindByIdAsync(_currentUser.UserId!);
        CurrentEmail = identityUser?.Email;

        var prefs = Profile.NotificationPreferences;
        if (prefs == null)
        {
            prefs = new NotificationPreferences { UserProfileId = Profile.Id, CreatedAt = DateTime.UtcNow };
            _context.NotificationPreferences.Add(prefs);
        }

        prefs.NewBidOnListing = Notifications.NewBidOnListing;
        prefs.BidOutbid = Notifications.BidOutbid;
        prefs.AuctionWon = Notifications.AuctionWon;
        prefs.NewOrder = Notifications.NewOrder;
        prefs.OrderShipped = Notifications.OrderShipped;
        prefs.OrderCompleted = Notifications.OrderCompleted;
        prefs.NewChatMessage = Notifications.NewChatMessage;
        prefs.DisputeUpdate = Notifications.DisputeUpdate;
        prefs.OfferUpdate = Notifications.OfferUpdate;

        await _context.SaveChangesAsync();

        TempData["Success"] = "Notification preferences saved.";
        return RedirectToPage();
    }
}
