using MaltasGarage.Application.Common.Interfaces;

namespace MaltasGarage.Tests.Stubs;

public class NoOpEmailNotificationService : IEmailNotificationService
{
    public Task NotifyBidOutbidAsync(Guid userProfileId, string listingTitle, decimal newBid, Guid listingId) => Task.CompletedTask;
    public Task NotifyNewBidOnListingAsync(Guid sellerProfileId, string listingTitle, decimal amount, Guid listingId) => Task.CompletedTask;
    public Task NotifyAuctionWonAsync(Guid buyerProfileId, string listingTitle, decimal amount, Guid orderId) => Task.CompletedTask;
    public Task NotifyNewOrderAsync(Guid sellerProfileId, string listingTitle, decimal amount, Guid orderId) => Task.CompletedTask;
    public Task NotifyOrderShippedAsync(Guid buyerProfileId, string listingTitle, string trackingNumber, Guid orderId) => Task.CompletedTask;
    public Task NotifyOrderCompletedSellerAsync(Guid sellerProfileId, string listingTitle, Guid orderId) => Task.CompletedTask;
    public Task NotifyOrderCompletedBuyerAsync(Guid buyerProfileId, string listingTitle, Guid orderId) => Task.CompletedTask;
    public Task NotifyNewChatMessageAsync(Guid recipientProfileId, string senderName, string preview, Guid conversationId) => Task.CompletedTask;
    public Task NotifyDisputeUpdateAsync(Guid userProfileId, string listingTitle, string updateText, Guid orderId) => Task.CompletedTask;
    public Task SendWelcomeEmailAsync(string email, string displayName, string confirmationUrl) => Task.CompletedTask;
    public Task NotifyAuctionExpiredAsync(Guid sellerProfileId, string listingTitle) => Task.CompletedTask;
    public Task SendOrderConfirmationAsync(Guid buyerProfileId, string listingTitle, decimal amount, Guid orderId) => Task.CompletedTask;
    public Task SendContactFormAsync(string fromName, string fromEmail, string subject, string message) => Task.CompletedTask;
    public Task NotifyDisputeOpenedToStaffAsync(string listingTitle, Guid orderId, string buyerName, string sellerName, string reason) => Task.CompletedTask;
    public Task SendAdminMessageAsync(string recipientUserId, string subject, string messageBody, Guid orderId) => Task.CompletedTask;
    public Task NotifyOfferReceivedAsync(Guid sellerProfileId, string listingTitle, decimal amount, Guid listingId) => Task.CompletedTask;
    public Task NotifyOfferAcceptedAsync(Guid buyerProfileId, string listingTitle, decimal amount, Guid listingId) => Task.CompletedTask;
    public Task NotifyOfferRejectedAsync(Guid buyerProfileId, string listingTitle) => Task.CompletedTask;
    public Task NotifyBundleOfferReceivedAsync(Guid sellerProfileId, int itemCount, decimal amount, Guid bundleOfferId) => Task.CompletedTask;
    public Task NotifyBundleOfferAcceptedAsync(Guid buyerProfileId, int itemCount, decimal amount, Guid bundleOfferId) => Task.CompletedTask;
    public Task NotifyBundleOfferRejectedAsync(Guid buyerProfileId, int itemCount, decimal amount) => Task.CompletedTask;
}
