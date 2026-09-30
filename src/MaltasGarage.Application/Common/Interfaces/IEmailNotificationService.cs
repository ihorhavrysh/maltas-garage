namespace MaltasGarage.Application.Common.Interfaces;

public interface IEmailNotificationService
{
    // Preference-based — only sent when user has opted in
    Task NotifyBidOutbidAsync(Guid userProfileId, string listingTitle, decimal newBid, Guid listingId);
    Task NotifyNewBidOnListingAsync(Guid sellerProfileId, string listingTitle, decimal amount, Guid listingId);
    Task NotifyAuctionWonAsync(Guid buyerProfileId, string listingTitle, decimal amount, Guid orderId);
    Task NotifyNewOrderAsync(Guid sellerProfileId, string listingTitle, decimal amount, Guid orderId);
    Task NotifyOrderShippedAsync(Guid buyerProfileId, string listingTitle, string trackingNumber, Guid orderId);
    Task NotifyOrderCompletedSellerAsync(Guid sellerProfileId, string listingTitle, Guid orderId);
    Task NotifyOrderCompletedBuyerAsync(Guid buyerProfileId, string listingTitle, Guid orderId);
    Task NotifyNewChatMessageAsync(Guid recipientProfileId, string senderName, string preview, Guid conversationId);
    Task NotifyDisputeUpdateAsync(Guid userProfileId, string listingTitle, string updateText, Guid orderId);
    Task NotifyOfferReceivedAsync(Guid sellerProfileId, string listingTitle, decimal amount, Guid listingId);
    Task NotifyOfferAcceptedAsync(Guid buyerProfileId, string listingTitle, decimal amount, Guid listingId);
    Task NotifyOfferRejectedAsync(Guid buyerProfileId, string listingTitle);
    Task NotifyBundleOfferReceivedAsync(Guid sellerProfileId, int itemCount, decimal amount, Guid bundleOfferId);
    Task NotifyBundleOfferAcceptedAsync(Guid buyerProfileId, int itemCount, decimal amount, Guid bundleOfferId);
    Task NotifyBundleOfferRejectedAsync(Guid buyerProfileId, int itemCount, decimal amount);

    // Transactional — always sent regardless of preferences
    Task SendWelcomeEmailAsync(string email, string displayName, string confirmationUrl);
    Task NotifyAuctionExpiredAsync(Guid sellerProfileId, string listingTitle);
    Task SendOrderConfirmationAsync(Guid buyerProfileId, string listingTitle, decimal amount, Guid orderId);
    Task SendContactFormAsync(string fromName, string fromEmail, string subject, string message);
    Task NotifyDisputeOpenedToStaffAsync(string listingTitle, Guid orderId, string buyerName, string sellerName, string reason);
    /// <summary>Sends a direct email from admin/support to a specific user (by IdentityUser ID).</summary>
    Task SendAdminMessageAsync(string recipientUserId, string subject, string messageBody, Guid orderId);
}
