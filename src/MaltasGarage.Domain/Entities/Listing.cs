using MaltasGarage.Domain.Common;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Domain.Exceptions;

namespace MaltasGarage.Domain.Entities;

public class Listing : BaseEntity, IConcurrencyStamped
{
    public Guid ConcurrencyStamp { get; set; }

    public Guid SellerId { get; set; }
    public Guid CategoryId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    // Condition
    public bool IsNew { get; set; }

    // Pricing
    public decimal DesiredPrice { get; set; }
    public decimal MinPrice { get; set; }
    public decimal CurrentPrice { get; set; }

    // Dates
    public DateTime SellByDate { get; set; }
    public DateTime? AuctionStartDate { get; set; } // null when auction is disabled

    // Auction
    public bool AuctionEnabled { get; set; } = true;

    public ListingStatus Status { get; set; } = ListingStatus.Draft;

    // Demo: permanent showcase listings are read-only for everyone and are skipped by
    // the expiry and completion jobs. See EnsureNotShowcase().
    public bool IsShowcase { get; set; }

    // Location
    public string? PickupAddress { get; set; }
    public string? PickupCity { get; set; }

    // Stats
    public int ViewCount { get; set; }
    public DateTime? PublishedAt { get; set; }

    // Navigation properties
    public UserProfile Seller { get; set; } = null!;
    public Category Category { get; set; } = null!;
    public ICollection<ListingImage> Images { get; set; } = new List<ListingImage>();
    public ICollection<Bid> Bids { get; set; } = new List<Bid>();
    public Order? Order { get; set; }

    /// <summary>
    /// Guard for every mutating path (buy, bid, offer, edit, delete, relist, admin removal).
    /// </summary>
    public void EnsureNotShowcase()
    {
        if (IsShowcase)
            throw new ShowcaseListingException();
    }
}
