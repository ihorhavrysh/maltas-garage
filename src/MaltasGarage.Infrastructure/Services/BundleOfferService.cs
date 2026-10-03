using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Infrastructure.Services;

public class BundleOfferService : IBundleOfferService
{
    private readonly ApplicationDbContext _context;
    private readonly IMessagingService _messaging;
    private readonly IEmailNotificationService _emailNotifications;

    public BundleOfferService(ApplicationDbContext context, IMessagingService messaging, IEmailNotificationService emailNotifications)
    {
        _context = context;
        _messaging = messaging;
        _emailNotifications = emailNotifications;
    }

    public async Task<BundleOffer> SubmitBundleOfferAsync(Guid sellerId, Guid buyerProfileId, List<Guid> listingIds, decimal offerAmount)
    {
        if (listingIds.Count < 2)
            throw new InvalidOperationException("A bundle offer must include at least 2 listings.");

        if (offerAmount <= 0)
            throw new InvalidOperationException("Offer amount must be greater than zero.");

        if (buyerProfileId == sellerId)
            throw new InvalidOperationException("You cannot make an offer on your own listings.");

        var listings = await _context.Listings
            .Include(l => l.Seller)
            .Where(l => listingIds.Contains(l.Id))
            .ToListAsync();

        if (listings.Count != listingIds.Count)
            throw new InvalidOperationException("One or more listings were not found.");

        if (listings.Any(l => l.SellerId != sellerId))
            throw new InvalidOperationException("All listings must belong to the same seller.");

        listings.ForEach(l => l.EnsureNotShowcase());

        if (listings.Any(l => l.Status != ListingStatus.Active))
            throw new InvalidOperationException("All listings must be active.");

        var existing = await _context.BundleOffers
            .FirstOrDefaultAsync(o => o.SellerId == sellerId
                                   && o.BuyerId == buyerProfileId
                                   && (o.Status == BundleOfferStatus.Pending || o.Status == BundleOfferStatus.Accepted));
        if (existing != null)
            throw new InvalidOperationException("You already have an active bundle offer with this seller.");

        var conversation = await _messaging.GetOrCreateConversationAsync(buyerProfileId, sellerId);

        var bundleOffer = new BundleOffer
        {
            Id               = Guid.NewGuid(),
            BuyerId          = buyerProfileId,
            SellerId         = sellerId,
            OfferAmount      = offerAmount,
            TotalListedPrice = listings.Sum(l => l.DesiredPrice),
            Status           = BundleOfferStatus.Pending,
            ExpiresAt        = DateTime.UtcNow.AddHours(24),
            ConversationId   = conversation.Id,
            CreatedAt        = DateTime.UtcNow
        };

        _context.BundleOffers.Add(bundleOffer);
        await _context.SaveChangesAsync();

        foreach (var listing in listings)
        {
            _context.BundleOfferItems.Add(new BundleOfferItem
            {
                Id            = Guid.NewGuid(),
                BundleOfferId = bundleOffer.Id,
                ListingId     = listing.Id,
                ListedPrice   = listing.DesiredPrice,
                CreatedAt     = DateTime.UtcNow
            });
        }

        var chatMsg = new ChatMessage
        {
            Id              = Guid.NewGuid(),
            ConversationId  = conversation.Id,
            FromUserId      = buyerProfileId,
            Body            = $"Bundle offer of €{offerAmount:N0} for {listings.Count} items",
            IsSystemNote    = true,
            BundleOfferRef  = bundleOffer.Id,
            IsRead          = false,
            CreatedAt       = DateTime.UtcNow
        };
        _context.ChatMessages.Add(chatMsg);
        conversation.UpdatedAt = DateTime.UtcNow;

        await _messaging.CreateSystemMessageAsync(
            sellerId,
            SystemMessageType.OfferReceived,
            $"Bundle offer of €{offerAmount:N0} for {listings.Count} items.",
            $"/Messages?c={conversation.Id}");

        await _context.SaveChangesAsync();

        await _emailNotifications.NotifyBundleOfferReceivedAsync(sellerId, listings.Count, offerAmount, bundleOffer.Id);

        return bundleOffer;
    }

    public async Task AcceptBundleOfferAsync(Guid bundleOfferId, Guid sellerProfileId)
    {
        var offer = await _context.BundleOffers
            .Include(o => o.Items).ThenInclude(i => i.Listing)
            .FirstOrDefaultAsync(o => o.Id == bundleOfferId)
            ?? throw new InvalidOperationException("Bundle offer not found.");

        if (offer.SellerId != sellerProfileId)
            throw new InvalidOperationException("Only the seller can accept this offer.");

        offer.Items.ToList().ForEach(i => i.Listing.EnsureNotShowcase());

        if (offer.Status != BundleOfferStatus.Pending)
            throw new InvalidOperationException("This offer is no longer pending.");

        // Evaluate on read: an offer past its expiry cannot be accepted, sweep or no sweep
        if (offer.ExpiresAt <= DateTime.UtcNow)
        {
            await ExpireBundleOfferAsync(offer.Id);
            throw new InvalidOperationException("This offer has expired.");
        }

        offer.Status    = BundleOfferStatus.Accepted;
        offer.UpdatedAt = DateTime.UtcNow;

        if (offer.ConversationId.HasValue)
        {
            var conversation = await _context.Conversations.FindAsync(offer.ConversationId.Value);
            if (conversation != null) conversation.UpdatedAt = DateTime.UtcNow;
        }

        await _messaging.CreateSystemMessageAsync(
            offer.BuyerId,
            SystemMessageType.OfferAccepted,
            $"Your bundle offer of €{offer.OfferAmount:N0} for {offer.Items.Count} items was accepted! Open the chat to complete the purchase.",
            offer.ConversationId.HasValue ? $"/Messages?c={offer.ConversationId}" : null);

        await _context.SaveChangesAsync();

        await _emailNotifications.NotifyBundleOfferAcceptedAsync(offer.BuyerId, offer.Items.Count, offer.OfferAmount, offer.Id);
    }

    public async Task RejectBundleOfferAsync(Guid bundleOfferId, Guid sellerProfileId)
    {
        var offer = await _context.BundleOffers
            .Include(o => o.Items).ThenInclude(i => i.Listing)
            .FirstOrDefaultAsync(o => o.Id == bundleOfferId)
            ?? throw new InvalidOperationException("Bundle offer not found.");

        if (offer.SellerId != sellerProfileId)
            throw new InvalidOperationException("Only the seller can reject this offer.");

        offer.Items.ToList().ForEach(i => i.Listing.EnsureNotShowcase());

        if (offer.Status != BundleOfferStatus.Pending)
            throw new InvalidOperationException("This offer is no longer pending.");

        await SetClosedAsync(offer, BundleOfferStatus.Rejected,
            $"Your bundle offer of €{offer.OfferAmount:N0} for {offer.Items.Count} items was declined.");
    }

    public async Task ExpireBundleOfferAsync(Guid bundleOfferId)
    {
        var offer = await _context.BundleOffers
            .Include(o => o.Items).ThenInclude(i => i.Listing)
            .FirstOrDefaultAsync(o => o.Id == bundleOfferId)
            ?? throw new InvalidOperationException("Bundle offer not found.");

        // Showcase bundle offers stay pending forever so the demo always has one to show
        if (offer.Status != BundleOfferStatus.Pending || offer.Items.Any(i => i.Listing.IsShowcase)) return;

        await SetClosedAsync(offer, BundleOfferStatus.Expired,
            $"Your bundle offer of €{offer.OfferAmount:N0} for {offer.Items.Count} items expired - the seller didn't respond in time.");
    }

    public async Task CancelBundleOffersForListingAsync(Guid listingId)
    {
        if (await _context.Listings.AnyAsync(l => l.Id == listingId && l.IsShowcase)) return;

        var affectedOfferIds = await _context.BundleOfferItems
            .Where(i => i.ListingId == listingId)
            .Select(i => i.BundleOfferId)
            .Distinct()
            .ToListAsync();

        if (affectedOfferIds.Count == 0) return;

        var pendingOffers = await _context.BundleOffers
            .Include(o => o.Items)
            .Where(o => affectedOfferIds.Contains(o.Id) && o.Status == BundleOfferStatus.Pending)
            .ToListAsync();

        foreach (var offer in pendingOffers)
        {
            offer.Status    = BundleOfferStatus.Cancelled;
            offer.UpdatedAt = DateTime.UtcNow;

            await _messaging.CreateSystemMessageAsync(
                offer.BuyerId,
                SystemMessageType.OfferRejected,
                $"Your bundle offer of €{offer.OfferAmount:N0} for {offer.Items.Count} items was cancelled - one or more items are no longer available.",
                offer.ConversationId.HasValue ? $"/Messages?c={offer.ConversationId}" : null);
        }

        if (pendingOffers.Count > 0)
            await _context.SaveChangesAsync();
    }

    public async Task<BundleOffer?> GetActiveBundleOfferForBuyerAsync(Guid sellerId, Guid buyerProfileId)
    {
        return await _context.BundleOffers
            .Include(o => o.Items).ThenInclude(i => i.Listing)
            .FirstOrDefaultAsync(o => o.SellerId == sellerId
                                   && o.BuyerId == buyerProfileId
                                   && (o.Status == BundleOfferStatus.Pending || o.Status == BundleOfferStatus.Accepted));
    }

    private async Task SetClosedAsync(BundleOffer offer, BundleOfferStatus status, string buyerMessage)
    {
        offer.Status    = status;
        offer.UpdatedAt = DateTime.UtcNow;

        if (offer.ConversationId.HasValue)
        {
            var conversation = await _context.Conversations.FindAsync(offer.ConversationId.Value);
            if (conversation != null) conversation.UpdatedAt = DateTime.UtcNow;
        }

        await _messaging.CreateSystemMessageAsync(
            offer.BuyerId,
            SystemMessageType.OfferRejected,
            buyerMessage,
            offer.ConversationId.HasValue ? $"/Messages?c={offer.ConversationId}" : null);

        await _context.SaveChangesAsync();

        await _emailNotifications.NotifyBundleOfferRejectedAsync(offer.BuyerId, offer.Items.Count, offer.OfferAmount);
    }
}
