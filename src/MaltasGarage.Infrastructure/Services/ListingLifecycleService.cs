using System.Linq.Expressions;
using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Common;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MaltasGarage.Infrastructure.Services;

/// <inheritdoc cref="IListingLifecycleService"/>
public class ListingLifecycleService : IListingLifecycleService
{
    private enum Transition { None, AuctionOpened, Expired, AuctionClosed }

    private readonly ApplicationDbContext _context;
    private readonly IPriceOfferService _priceOffers;
    private readonly IBundleOfferService _bundleOffers;
    private readonly IOrderService _orders;
    private readonly IMessagingService _messaging;
    private readonly IEmailNotificationService _emailNotifications;
    private readonly TimeProvider _time;
    private readonly ILogger<ListingLifecycleService> _logger;

    public ListingLifecycleService(
        ApplicationDbContext context,
        IPriceOfferService priceOffers,
        IBundleOfferService bundleOffers,
        IOrderService orders,
        IMessagingService messaging,
        IEmailNotificationService emailNotifications,
        TimeProvider time,
        ILogger<ListingLifecycleService> logger)
    {
        _context = context;
        _priceOffers = priceOffers;
        _bundleOffers = bundleOffers;
        _orders = orders;
        _messaging = messaging;
        _emailNotifications = emailNotifications;
        _time = time;
        _logger = logger;
    }

    /// <summary>A listing has a transition due: it ended, or its auction phase has started.</summary>
    private static Expression<Func<Listing, bool>> IsDue(DateTime now) => l =>
        !l.IsShowcase && (
            ((l.Status == ListingStatus.Active || l.Status == ListingStatus.AuctionPhase) && l.SellByDate <= now) ||
            (l.Status == ListingStatus.Active && l.AuctionEnabled &&
             l.AuctionStartDate != null && l.AuctionStartDate <= now && l.SellByDate > now));

    public async Task EnsureCurrentAsync(Guid listingId, CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        // One cheap indexed lookup on the hot path; the full load only happens when something is due
        if (await _context.Listings.Where(l => l.Id == listingId).AnyAsync(IsDue(now), cancellationToken))
            await ApplyAsync(listingId, now, cancellationToken);
    }

    public async Task<LifecycleSweepResult> SweepAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        var dueListings = await _context.Listings.Where(IsDue(now)).Select(l => l.Id).ToListAsync(cancellationToken);
        int opened = 0, expired = 0, closed = 0;
        foreach (var id in dueListings)
        {
            switch (await ApplyAsync(id, now, cancellationToken))
            {
                case Transition.AuctionOpened: opened++; break;
                case Transition.Expired: expired++; break;
                case Transition.AuctionClosed: closed++; break;
            }
        }

        var offersExpired = await ExpireDueOffersAsync(now, cancellationToken);
        var escrowsReleased = await _orders.AutoReleaseDueEscrowAsync(now);

        var result = new LifecycleSweepResult(opened, expired, closed, offersExpired, escrowsReleased);
        if (result.Total > 0)
            _logger.LogInformation("Lifecycle sweep applied {@Result}", result);
        return result;
    }

    // ── One listing ──────────────────────────────────────────────────────────

    private async Task<Transition> ApplyAsync(Guid listingId, DateTime now, CancellationToken cancellationToken)
    {
        try
        {
            var listing = await _context.Listings
                .Include(l => l.Bids)
                .FirstOrDefaultAsync(l => l.Id == listingId, cancellationToken);
            if (listing == null || listing.IsShowcase)
                return Transition.None;

            var previousStatus = listing.Status;
            var winningBid = listing.Bids.OrderByDescending(b => b.Amount).FirstOrDefault();
            Order? order = null;
            Transition transition;

            if (listing.SellByDate <= now && previousStatus is ListingStatus.Active or ListingStatus.AuctionPhase)
            {
                if (previousStatus == ListingStatus.AuctionPhase && winningBid != null)
                {
                    order = CloseAuction(listing, winningBid, now);
                    transition = Transition.AuctionClosed;
                }
                else
                {
                    listing.Status = ListingStatus.Expired;
                    transition = Transition.Expired;
                }
            }
            else if (previousStatus == ListingStatus.Active && listing.AuctionEnabled &&
                     listing.AuctionStartDate <= now && listing.SellByDate > now)
            {
                listing.Status = ListingStatus.AuctionPhase;
                listing.CurrentPrice = listing.MinPrice;
                transition = Transition.AuctionOpened;
            }
            else
            {
                return Transition.None;
            }

            // Status change, winning bid, order and payment are one SaveChanges, i.e. one
            // transaction. The listing's concurrency stamp makes it a claim: if anyone changed
            // the listing since it was loaded, this throws and nothing is written.
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                _context.ChangeTracker.Clear();
                _logger.LogDebug("Listing {ListingId} was updated concurrently; transition skipped", listingId);
                return Transition.None;
            }

            await AfterTransitionAsync(listing, previousStatus, transition, winningBid, order);
            return transition;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One bad listing must not stop the sweep for the others
            _logger.LogError(ex, "Lifecycle transition failed for listing {ListingId}", listingId);
            _context.ChangeTracker.Clear();
            return Transition.None;
        }
    }

    private Order CloseAuction(Listing listing, Bid winningBid, DateTime now)
    {
        winningBid.IsWinningBid = true;
        listing.Status = ListingStatus.Sold;
        listing.CurrentPrice = winningBid.Amount;

        // The winner paid when bidding, so with a payment intent the order starts as Paid
        var paid = !string.IsNullOrEmpty(winningBid.StripePaymentIntentId);
        var fee = PlatformFee.Calculate(winningBid.Amount);
        var order = new Order
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = winningBid.BidderId,
            SellerId = listing.SellerId,
            FinalPrice = winningBid.Amount,
            PlatformFee = fee,
            SellerPayout = winningBid.Amount - fee,
            Status = paid ? OrderStatus.Paid : OrderStatus.Pending,
            PaidAt = paid ? now : null,
            DeliveryMethod = DeliveryMethod.HandToHand, // placeholder until the buyer chooses
            DeliveryMethodPending = true,
            CreatedAt = now
        };
        _context.Orders.Add(order);

        if (paid)
        {
            _context.Payments.Add(new Payment
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                StripePaymentIntentId = winningBid.StripePaymentIntentId,
                Amount = winningBid.Amount,
                Status = PaymentStatus.Captured,
                CapturedAt = now,
                CreatedAt = now
            });
        }

        return order;
    }

    /// <summary>Side effects run only after the transition is committed, and only once.</summary>
    private async Task AfterTransitionAsync(Listing listing, ListingStatus previousStatus,
        Transition transition, Bid? winningBid, Order? order)
    {
        if (transition is Transition.AuctionOpened or Transition.AuctionClosed)
        {
            // Offers only make sense before the auction phase
            await _priceOffers.CancelOpenOffersForListingAsync(listing.Id);
            await _bundleOffers.CancelBundleOffersForListingAsync(listing.Id);
        }

        if (transition == Transition.Expired && previousStatus == ListingStatus.AuctionPhase)
        {
            await _messaging.CreateSystemMessageAsync(listing.SellerId, SystemMessageType.AuctionExpired,
                $"Your auction for \"{listing.Title}\" has ended with no bids. You can re-list the item at any time.",
                $"/Listing/{listing.Id}");
            await _emailNotifications.NotifyAuctionExpiredAsync(listing.SellerId, listing.Title);
        }

        if (transition == Transition.AuctionClosed && winningBid != null && order != null)
        {
            await _messaging.CreateSystemMessageAsync(winningBid.BidderId, SystemMessageType.AuctionWon,
                $"You won the auction for \"{listing.Title}\" with a bid of €{winningBid.Amount:N0}!",
                $"/Orders/Details/{order.Id}");
            await _emailNotifications.NotifyAuctionWonAsync(winningBid.BidderId, listing.Title, winningBid.Amount, order.Id);

            await _messaging.CreateSystemMessageAsync(listing.SellerId, SystemMessageType.AuctionSold,
                $"Your auction for \"{listing.Title}\" has sold! Winning bid: €{winningBid.Amount:N0}.",
                $"/Orders/Details/{order.Id}");
            await _emailNotifications.NotifyNewOrderAsync(listing.SellerId, listing.Title, winningBid.Amount, order.Id);
        }
    }

    // ── Offers ───────────────────────────────────────────────────────────────

    private async Task<int> ExpireDueOffersAsync(DateTime now, CancellationToken cancellationToken)
    {
        var priceOfferIds = await _context.PriceOffers
            .Where(o => (o.Status == PriceOfferStatus.Pending || o.Status == PriceOfferStatus.Accepted) && o.ExpiresAt <= now && !o.Listing.IsShowcase)
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);

        var bundleOfferIds = await _context.BundleOffers
            .Where(o => o.Status == BundleOfferStatus.Pending && o.ExpiresAt <= now && !o.Items.Any(i => i.Listing.IsShowcase))
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);

        // ExpireOfferAsync re-checks the status, so an offer handled meanwhile is skipped
        foreach (var id in priceOfferIds)
        {
            try { await _priceOffers.ExpireOfferAsync(id); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to expire price offer {OfferId}", id); }
        }

        foreach (var id in bundleOfferIds)
        {
            try { await _bundleOffers.ExpireBundleOfferAsync(id); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to expire bundle offer {BundleOfferId}", id); }
        }

        return priceOfferIds.Count + bundleOfferIds.Count;
    }
}
