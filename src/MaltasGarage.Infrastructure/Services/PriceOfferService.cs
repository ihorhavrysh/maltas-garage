using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Infrastructure.Services;

public class PriceOfferService : IPriceOfferService
{
    private readonly ApplicationDbContext _context;
    private readonly IMessagingService _messaging;
    private readonly IEmailNotificationService _emailNotifications;
    private readonly TimeProvider _time;

    public PriceOfferService(
        ApplicationDbContext context,
        IMessagingService messaging,
        IEmailNotificationService emailNotifications,
        TimeProvider time)
    {
        _context = context;
        _messaging = messaging;
        _emailNotifications = emailNotifications;
        _time = time;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    /// <summary>How long a buyer has to pay once the seller accepts their offer.</summary>
    public static readonly TimeSpan AcceptedOfferPayWindow = TimeSpan.FromHours(48);

    public async Task<PriceOffer> SubmitOfferAsync(Guid listingId, Guid buyerProfileId, decimal amount)
    {
        var listing = await _context.Listings
            .Include(l => l.Seller)
            .FirstOrDefaultAsync(l => l.Id == listingId)
            ?? throw new InvalidOperationException("Listing not found.");

        listing.EnsureNotShowcase();

        if (listing.Status != ListingStatus.Active)
            throw new InvalidOperationException("Offers can only be made on active listings.");

        if (listing.SellerId == buyerProfileId)
            throw new InvalidOperationException("You cannot make an offer on your own listing.");

        if (amount < 1 || amount >= listing.DesiredPrice)
            throw new InvalidOperationException($"Offer must be between €1 and €{listing.DesiredPrice - 1:N0}.");

        var existing = await _context.PriceOffers
            .FirstOrDefaultAsync(o => o.ListingId == listingId
                                   && o.BuyerId == buyerProfileId
                                   && (o.Status == PriceOfferStatus.Pending || o.Status == PriceOfferStatus.Accepted));
        if (existing != null)
            throw new InvalidOperationException("You already have an active offer on this listing.");

        var conversation = await _messaging.GetOrCreateConversationAsync(buyerProfileId, listing.SellerId, Guid.Empty);

        var offer = new PriceOffer
        {
            Id             = Guid.NewGuid(),
            ListingId      = listingId,
            BuyerId        = buyerProfileId,
            SellerId       = listing.SellerId,
            Amount         = amount,
            Status         = PriceOfferStatus.Pending,
            ExpiresAt      = Now.AddHours(24),
            ConversationId = conversation.Id,
            CreatedAt      = Now
        };

        _context.PriceOffers.Add(offer);
        await _context.SaveChangesAsync();

        // System note in chat (buyer sees it as confirmation, seller sees offer card)
        var chatMsg = new ChatMessage
        {
            Id             = Guid.NewGuid(),
            ConversationId = conversation.Id,
            FromUserId     = buyerProfileId,
            Body           = $"Offer of €{amount:N0} for \"{listing.Title}\"",
            IsSystemNote   = true,
            OfferRef       = offer.Id,
            IsRead         = false,
            CreatedAt      = Now
        };
        _context.ChatMessages.Add(chatMsg);

        // Touch conversation so it bubbles to top
        conversation.UpdatedAt = Now;

        // Bell notification for seller
        await _messaging.CreateSystemMessageAsync(
            listing.SellerId,
            SystemMessageType.OfferReceived,
            $"New offer of €{amount:N0} on your listing \"{listing.Title}\".",
            $"/Messages?c={conversation.Id}");

        await _context.SaveChangesAsync();

        await _emailNotifications.NotifyOfferReceivedAsync(listing.SellerId, listing.Title, amount, listingId);

        return offer;
    }

    public async Task AcceptOfferAsync(Guid offerId, Guid sellerProfileId)
    {
        var offer = await _context.PriceOffers
            .Include(o => o.Listing)
            .FirstOrDefaultAsync(o => o.Id == offerId)
            ?? throw new InvalidOperationException("Offer not found.");

        if (offer.SellerId != sellerProfileId)
            throw new InvalidOperationException("Only the seller can accept this offer.");

        offer.Listing.EnsureNotShowcase();

        if (offer.Status != PriceOfferStatus.Pending)
            throw new InvalidOperationException("This offer is no longer pending.");

        // Evaluate on read: an offer past its expiry cannot be accepted, sweep or no sweep
        if (offer.ExpiresAt <= Now)
        {
            await ExpireOfferAsync(offer.Id);
            throw new InvalidOperationException("This offer has expired.");
        }

        offer.Status    = PriceOfferStatus.Accepted;
        offer.UpdatedAt = Now;
        // From now on ExpiresAt is the pay-by deadline: an accepted price does not hold forever
        offer.ExpiresAt = Now + AcceptedOfferPayWindow;

        if (offer.ConversationId.HasValue)
        {
            var conversation = await _context.Conversations.FindAsync(offer.ConversationId.Value);
            if (conversation != null) conversation.UpdatedAt = Now;
        }

        // Bell notification for buyer
        await _messaging.CreateSystemMessageAsync(
            offer.BuyerId,
            SystemMessageType.OfferAccepted,
            $"Your offer of €{offer.Amount:N0} for \"{offer.Listing.Title}\" was accepted! Open the chat to complete the purchase.",
            offer.ConversationId.HasValue ? $"/Messages?c={offer.ConversationId}" : $"/Listing/{offer.ListingId}");

        await _context.SaveChangesAsync();

        await _emailNotifications.NotifyOfferAcceptedAsync(offer.BuyerId, offer.Listing.Title, offer.Amount, offer.ListingId);
    }

    public async Task RejectOfferAsync(Guid offerId, Guid sellerProfileId)
    {
        var offer = await _context.PriceOffers
            .Include(o => o.Listing)
            .FirstOrDefaultAsync(o => o.Id == offerId)
            ?? throw new InvalidOperationException("Offer not found.");

        if (offer.SellerId != sellerProfileId)
            throw new InvalidOperationException("Only the seller can reject this offer.");

        offer.Listing.EnsureNotShowcase();

        if (offer.Status != PriceOfferStatus.Pending)
            throw new InvalidOperationException("This offer is no longer pending.");

        await SetOfferClosedAsync(offer, PriceOfferStatus.Rejected,
            $"Your offer of €{offer.Amount:N0} for \"{offer.Listing.Title}\" was declined. Try a different price or browse other listings.");
    }

    public async Task ExpireOfferAsync(Guid offerId)
    {
        var offer = await _context.PriceOffers
            .Include(o => o.Listing)
            .FirstOrDefaultAsync(o => o.Id == offerId)
            ?? throw new InvalidOperationException("Offer not found.");

        // Showcase offers stay pending forever so the demo always has one to show
        if (offer.Listing.IsShowcase) return;

        if (offer.Status == PriceOfferStatus.Accepted)
        {
            await SetOfferClosedAsync(offer, PriceOfferStatus.Expired,
                $"The accepted offer of €{offer.Amount:N0} for \"{offer.Listing.Title}\" expired - it was not paid within {AcceptedOfferPayWindow.TotalHours:N0} hours.");
            return;
        }

        if (offer.Status != PriceOfferStatus.Pending) return;

        await SetOfferClosedAsync(offer, PriceOfferStatus.Expired,
            $"Your offer of €{offer.Amount:N0} for \"{offer.Listing.Title}\" expired - the seller didn't respond in time.");
    }

    public async Task CancelOpenOffersForListingAsync(Guid listingId)
    {
        var listing = await _context.Listings.FindAsync(listingId);
        if (listing?.IsShowcase == true) return;
        var title = listing?.Title ?? "this listing";

        // Find who actually bought this listing (if anyone) to skip notifying them
        var actualBuyerId = await _context.Orders
            .Where(o => o.ListingId == listingId && o.Status != OrderStatus.Refunded)
            .Select(o => (Guid?)o.BuyerId)
            .FirstOrDefaultAsync();

        // Pending and accepted offers both stop making sense once the item is sold or the
        // auction starts: an accepted price must not let someone buy below the auction later
        var open = await _context.PriceOffers
            .Where(o => o.ListingId == listingId &&
                        (o.Status == PriceOfferStatus.Pending || o.Status == PriceOfferStatus.Accepted))
            .ToListAsync();

        var reason = listing?.Status == ListingStatus.Sold
            ? "the item has been sold"
            : listing?.Status == ListingStatus.AuctionPhase
                ? "the listing has moved to its auction phase, where you can bid instead"
                : "the listing is no longer available";

        foreach (var offer in open)
        {
            offer.Status    = PriceOfferStatus.Cancelled;
            offer.UpdatedAt = Now;

            // Don't notify the buyer who actually purchased the item
            if (offer.BuyerId == actualBuyerId)
                continue;

            await _messaging.CreateSystemMessageAsync(
                offer.BuyerId,
                SystemMessageType.OfferRejected,
                $"Your offer of €{offer.Amount:N0} for \"{title}\" was cancelled - {reason}.",
                $"/Listing/{listingId}");

            await _emailNotifications.NotifyOfferRejectedAsync(offer.BuyerId, title);
        }

        if (open.Count > 0)
            await _context.SaveChangesAsync();
    }

    public async Task<PriceOffer?> GetActiveOfferForBuyerAsync(Guid listingId, Guid buyerProfileId)
    {
        return await _context.PriceOffers
            .FirstOrDefaultAsync(o => o.ListingId == listingId
                                   && o.BuyerId == buyerProfileId
                                   && (o.Status == PriceOfferStatus.Pending || o.Status == PriceOfferStatus.Accepted));
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task SetOfferClosedAsync(PriceOffer offer, PriceOfferStatus status, string buyerMessage)
    {
        offer.Status    = status;
        offer.UpdatedAt = Now;

        if (offer.ConversationId.HasValue)
        {
            var conversation = await _context.Conversations.FindAsync(offer.ConversationId.Value);
            if (conversation != null) conversation.UpdatedAt = Now;
        }

        await _messaging.CreateSystemMessageAsync(
            offer.BuyerId,
            SystemMessageType.OfferRejected,
            buyerMessage,
            offer.ConversationId.HasValue ? $"/Messages?c={offer.ConversationId}" : $"/Listing/{offer.ListingId}");

        await _context.SaveChangesAsync();

        await _emailNotifications.NotifyOfferRejectedAsync(offer.BuyerId, offer.Listing.Title);
    }
}
