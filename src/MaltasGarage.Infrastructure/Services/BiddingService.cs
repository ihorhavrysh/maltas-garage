using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Domain.Exceptions;
using MaltasGarage.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Infrastructure.Services;

public class BiddingService : IBiddingService
{
    private readonly ApplicationDbContext _context;
    private readonly IMessagingService _messaging;
    private readonly IEmailNotificationService _emailNotifications;

    public static decimal GetBidIncrement(decimal currentPrice) => currentPrice switch
    {
        < 50m    => 1m,
        < 200m   => 5m,
        < 500m   => 10m,
        < 1000m  => 25m,
        _        => 50m
    };

    public BiddingService(ApplicationDbContext context, IMessagingService messaging, IEmailNotificationService emailNotifications)
    {
        _context = context;
        _messaging = messaging;
        _emailNotifications = emailNotifications;
    }

    public async Task<BidResult> PlaceBidAsync(Guid listingId, Guid bidderId, decimal amount, string? paymentIntentId = null)
    {
        var listing = await _context.Listings
            .Include(l => l.Bids)
            .FirstOrDefaultAsync(l => l.Id == listingId);

        if (listing == null)
            return new BidResult { Success = false, Error = "Listing not found" };

        if (listing.IsShowcase)
            return new BidResult { Success = false, Error = ShowcaseListingException.DefaultMessage };

        if (listing.Status != ListingStatus.AuctionPhase)
            return new BidResult { Success = false, Error = "Auction is not active" };

        if (listing.SellByDate <= DateTime.UtcNow)
            return new BidResult { Success = false, Error = "Auction has ended" };

        if (listing.SellerId == bidderId)
            return new BidResult { Success = false, Error = "You cannot bid on your own listing" };

        var currentHighBid = listing.Bids.Any()
            ? listing.Bids.Max(b => b.Amount)
            : listing.MinPrice;

        // Prevent bidding if you're already the top bidder
        var currentTopBid = listing.Bids.OrderByDescending(b => b.Amount).FirstOrDefault();
        if (currentTopBid?.BidderId == bidderId)
            return new BidResult { Success = false, Error = "You are already the highest bidder" };

        // Find current top bidder (to notify of outbid)
        var previousTopBid = listing.Bids.Any()
            ? listing.Bids.OrderByDescending(b => b.Amount).First()
            : null;

        var increment = GetBidIncrement(currentHighBid);
        var minimumBid = currentHighBid + increment;
        if (amount < minimumBid)
            return new BidResult { Success = false, Error = $"Minimum bid is €{minimumBid:N0}" };

        if (amount >= listing.DesiredPrice)
            return new BidResult { Success = false, Error = "Bid meets or exceeds Buy Now price - consider buying directly." };

        var bid = new Bid
        {
            Id = Guid.NewGuid(),
            ListingId = listingId,
            BidderId = bidderId,
            Amount = amount,
            IsWinningBid = false,
            StripePaymentIntentId = paymentIntentId,
            CreatedAt = DateTime.UtcNow
        };

        _context.Bids.Add(bid);

        listing.CurrentPrice = amount;
        listing.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another bid (or the auction closing) changed the listing since it was loaded
            _context.ChangeTracker.Clear();
            return new BidResult { Success = false, Error = "The auction changed while you were bidding. Please refresh and try again." };
        }

        // Notify outbid user
        if (previousTopBid != null && previousTopBid.BidderId != bidderId)
        {
            await _messaging.CreateSystemMessageAsync(
                previousTopBid.BidderId,
                Domain.Enums.SystemMessageType.BidOutbid,
                $"You've been outbid on \"{listing.Title}\". New high bid: €{amount:N0}.",
                $"/Listing/{listing.Id}");
            await _emailNotifications.NotifyBidOutbidAsync(previousTopBid.BidderId, listing.Title, amount, listing.Id);
        }

        // Notify seller of new bid
        await _messaging.CreateSystemMessageAsync(
            listing.SellerId,
            Domain.Enums.SystemMessageType.NewBidOnListing,
            $"New bid of €{amount:N0} placed on your listing \"{listing.Title}\".",
            $"/Listing/{listing.Id}");
        await _emailNotifications.NotifyNewBidOnListingAsync(listing.SellerId, listing.Title, amount, listing.Id);

        return new BidResult { Success = true, NewHighBid = amount };
    }

    public async Task<decimal?> GetCurrentBidAsync(Guid listingId)
    {
        var maxBid = await _context.Bids
            .Where(b => b.ListingId == listingId)
            .MaxAsync(b => (decimal?)b.Amount);

        if (maxBid == null)
        {
            var listing = await _context.Listings.FindAsync(listingId);
            return listing?.MinPrice;
        }

        return maxBid;
    }

    public async Task<List<Bid>> GetBidsForListingAsync(Guid listingId)
    {
        return await _context.Bids
            .Include(b => b.Bidder)
            .Where(b => b.ListingId == listingId)
            .OrderByDescending(b => b.Amount)
            .ToListAsync();
    }

    public async Task<Bid?> GetWinningBidAsync(Guid listingId)
    {
        return await _context.Bids
            .Include(b => b.Bidder)
            .Where(b => b.ListingId == listingId)
            .OrderByDescending(b => b.Amount)
            .FirstOrDefaultAsync();
    }
}
