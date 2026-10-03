using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Pages.Messages;

[Authorize]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IMessagingService _messaging;
    private readonly IPriceOfferService _priceOfferService;
    private readonly IBundleOfferService _bundleOfferService;

    public IndexModel(ApplicationDbContext context, ICurrentUserService currentUser, IMessagingService messaging,
        IPriceOfferService priceOfferService, IBundleOfferService bundleOfferService)
    {
        _context             = context;
        _currentUser         = currentUser;
        _messaging           = messaging;
        _priceOfferService   = priceOfferService;
        _bundleOfferService  = bundleOfferService;
    }

    // Current user's profile
    public UserProfile? CurrentProfile { get; set; }

    // Left panel
    public List<ConversationSummary> Conversations { get; set; } = new();
    public int UnreadSystemCount { get; set; }

    // Right panel - chat
    public Conversation? ActiveConversation { get; set; }
    public List<ChatMessage> ActiveMessages { get; set; } = new();

    // Right panel - system
    public List<SystemMessage> SystemMessages { get; set; } = new();

    // Query params
    public Guid? ActiveConversationId { get; set; }
    public bool ShowSystem { get; set; }
    public Guid? PendingOrderRef { get; set; }

    // Offer cards in thread
    public Dictionary<Guid, PriceOffer> OffersInThread { get; set; } = new();
    public Dictionary<Guid, Domain.Entities.BundleOffer> BundleOffersInThread { get; set; } = new();

    [BindProperty]
    public Guid ConversationId { get; set; }

    [BindProperty]
    public string Body { get; set; } = string.Empty;

    [BindProperty]
    public Guid? FromOrder { get; set; }

    [BindProperty]
    public Guid OfferId { get; set; }

    public class ConversationSummary
    {
        public Guid Id { get; set; }
        public string OtherPartyName { get; set; } = string.Empty;
        public string? OtherPartyAvatarUrl { get; set; }
        public string? LastMessage { get; set; }
        public DateTime LastActivity { get; set; }
        public int UnreadCount { get; set; }
        public bool IsActive { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(Guid? c, bool? system, Guid? orderId, Guid? fromOrder)
    {
        CurrentProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (CurrentProfile == null)
            return RedirectToPage("/Index");

        // Handle ?orderId redirect - get or create conversation then redirect
        if (orderId.HasValue)
        {
            var order = await _context.Orders.FindAsync(orderId.Value);
            if (order == null) return NotFound();

            bool isBuyer = order.BuyerId == CurrentProfile.Id;
            bool isSeller = order.SellerId == CurrentProfile.Id;
            if (!isBuyer && !isSeller) return Forbid();

            var conv = await _messaging.GetOrCreateConversationAsync(order.BuyerId, order.SellerId);
            return RedirectToPage(new { c = conv.Id, fromOrder = order.Id });
        }

        // Default to system notifications when no tab is selected
        if (system == null && c == null)
            return RedirectToPage(new { system = true });

        ShowSystem = system == true;
        ActiveConversationId = c;
        PendingOrderRef = fromOrder;

        // Load all conversations for this user
        var rawConvs = await _context.Conversations
            .Include(cv => cv.Buyer)
            .Include(cv => cv.Seller)
            .Include(cv => cv.Messages)
            .Where(cv => cv.BuyerId == CurrentProfile.Id || cv.SellerId == CurrentProfile.Id)
            .OrderByDescending(cv => cv.UpdatedAt ?? cv.CreatedAt)
            .ToListAsync();

        Conversations = rawConvs.Select(cv =>
        {
            var other = cv.BuyerId == CurrentProfile.Id ? cv.Seller : cv.Buyer;
            var lastMsg = cv.Messages.OrderByDescending(m => m.CreatedAt).FirstOrDefault();
            var unread = cv.Messages.Count(m => m.FromUserId != CurrentProfile.Id && !m.IsRead);
            return new ConversationSummary
            {
                Id = cv.Id,
                OtherPartyName = other?.DisplayName ?? "User",
                OtherPartyAvatarUrl = other?.AvatarUrl,
                LastMessage = lastMsg?.Body,
                LastActivity = cv.UpdatedAt ?? cv.CreatedAt,
                UnreadCount = unread,
                IsActive = cv.Id == ActiveConversationId
            };
        }).ToList();

        // Unread system count
        UnreadSystemCount = await _context.SystemMessages
            .CountAsync(m => m.UserId == CurrentProfile.Id && !m.IsRead);

        // Load right panel
        if (ShowSystem)
        {
            SystemMessages = await _context.SystemMessages
                .Where(m => m.UserId == CurrentProfile.Id)
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync();

            await _messaging.MarkSystemMessagesReadAsync(CurrentProfile.Id);
        }
        else if (ActiveConversationId.HasValue)
        {
            ActiveConversation = await _context.Conversations
                .Include(cv => cv.Buyer)
                .Include(cv => cv.Seller)
                .FirstOrDefaultAsync(cv => cv.Id == ActiveConversationId.Value);

            if (ActiveConversation != null &&
                (ActiveConversation.BuyerId == CurrentProfile.Id || ActiveConversation.SellerId == CurrentProfile.Id))
            {
                ActiveMessages = await _context.ChatMessages
                    .Include(m => m.FromUser)
                    .Where(m => m.ConversationId == ActiveConversationId.Value)
                    .OrderBy(m => m.CreatedAt)
                    .ToListAsync();

                var offerIds = ActiveMessages
                    .Where(m => m.IsSystemNote && m.OfferRef.HasValue)
                    .Select(m => m.OfferRef!.Value)
                    .Distinct().ToList();

                if (offerIds.Count > 0)
                {
                    OffersInThread = await _context.PriceOffers
                        .Include(o => o.Listing)
                        .Where(o => offerIds.Contains(o.Id))
                        .ToDictionaryAsync(o => o.Id);
                }

                var bundleOfferIds = ActiveMessages
                    .Where(m => m.IsSystemNote && m.BundleOfferRef.HasValue)
                    .Select(m => m.BundleOfferRef!.Value)
                    .Distinct().ToList();

                if (bundleOfferIds.Count > 0)
                {
                    BundleOffersInThread = await _context.BundleOffers
                        .Include(o => o.Items).ThenInclude(i => i.Listing)
                        .Where(o => bundleOfferIds.Contains(o.Id))
                        .ToDictionaryAsync(o => o.Id);
                }

                await _messaging.MarkConversationReadAsync(ActiveConversationId.Value, CurrentProfile.Id);
            }
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        CurrentProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (CurrentProfile == null) return Forbid();
        if (string.IsNullOrWhiteSpace(Body)) return RedirectToPage(new { c = ConversationId });

        var conv = await _context.Conversations.FindAsync(ConversationId);
        if (conv == null) return NotFound();

        if (conv.BuyerId != CurrentProfile.Id && conv.SellerId != CurrentProfile.Id)
            return Forbid();

        // Insert order context note just before the first message from this order page visit
        if (FromOrder.HasValue)
        {
            var order = await _context.Orders
                .Include(o => o.Listing)
                .FirstOrDefaultAsync(o => o.Id == FromOrder.Value);

            if (order != null)
            {
                _context.ChatMessages.Add(new Domain.Entities.ChatMessage
                {
                    Id = Guid.NewGuid(),
                    ConversationId = ConversationId,
                    FromUserId = CurrentProfile.Id,
                    Body = order.Listing?.Title ?? "Item",
                    IsRead = true,
                    IsSystemNote = true,
                    OrderRef = FromOrder.Value,
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
            }
        }

        await _messaging.SendMessageAsync(ConversationId, CurrentProfile.Id, Body.Trim());

        return RedirectToPage(new { c = ConversationId });
    }

    public async Task<IActionResult> OnPostAcceptOfferAsync()
    {
        CurrentProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);
        if (CurrentProfile == null) return Forbid();

        try
        {
            await _priceOfferService.AcceptOfferAsync(OfferId, CurrentProfile.Id);
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        var offer = await _context.PriceOffers.FindAsync(OfferId);
        return RedirectToPage(new { c = offer?.ConversationId });
    }

    public async Task<IActionResult> OnPostRejectOfferAsync()
    {
        CurrentProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);
        if (CurrentProfile == null) return Forbid();

        try
        {
            await _priceOfferService.RejectOfferAsync(OfferId, CurrentProfile.Id);
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        var offer = await _context.PriceOffers.FindAsync(OfferId);
        return RedirectToPage(new { c = offer?.ConversationId });
    }

    [BindProperty]
    public Guid BundleOfferId { get; set; }

    public async Task<IActionResult> OnPostAcceptBundleOfferAsync()
    {
        CurrentProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);
        if (CurrentProfile == null) return Forbid();

        try
        {
            await _bundleOfferService.AcceptBundleOfferAsync(BundleOfferId, CurrentProfile.Id);
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        var offer = await _context.BundleOffers.FindAsync(BundleOfferId);
        return RedirectToPage(new { c = offer?.ConversationId });
    }

    public async Task<IActionResult> OnPostRejectBundleOfferAsync()
    {
        CurrentProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);
        if (CurrentProfile == null) return Forbid();

        try
        {
            await _bundleOfferService.RejectBundleOfferAsync(BundleOfferId, CurrentProfile.Id);
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        var offer = await _context.BundleOffers.FindAsync(BundleOfferId);
        return RedirectToPage(new { c = offer?.ConversationId });
    }
}
