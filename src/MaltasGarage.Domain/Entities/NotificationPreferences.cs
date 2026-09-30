using MaltasGarage.Domain.Common;

namespace MaltasGarage.Domain.Entities;

public class NotificationPreferences : BaseEntity
{
    public Guid UserProfileId { get; set; }

    // Preference-based — all off by default
    public bool NewBidOnListing { get; set; }   // Seller: someone bid on your listing
    public bool BidOutbid { get; set; }          // Buyer: you've been outbid
    public bool AuctionWon { get; set; }         // Buyer: you won the auction
    public bool NewOrder { get; set; }           // Seller: new order placed
    public bool OrderShipped { get; set; }       // Buyer: order shipped with tracking
    public bool OrderCompleted { get; set; }     // Both: order completed / escrow released
    public bool NewChatMessage { get; set; }     // Both: new message in chat
    public bool DisputeUpdate { get; set; }      // Both: dispute opened or resolved
    public bool OfferUpdate { get; set; }        // Both: price offer or bundle offer received/accepted/rejected

    public UserProfile UserProfile { get; set; } = null!;
}
