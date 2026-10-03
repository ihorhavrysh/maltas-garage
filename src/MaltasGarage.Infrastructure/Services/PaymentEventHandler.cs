using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Infrastructure.Services;

public class PaymentEventHandler : IPaymentEventHandler
{
    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _time;

    public PaymentEventHandler(ApplicationDbContext context, TimeProvider time)
    {
        _context = context;
        _time = time;
    }

    public async Task<bool> PaymentSucceededAsync(string paymentIntentId)
    {
        var payment = await _context.Payments
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.StripePaymentIntentId == paymentIntentId);

        // Only a payment still waiting for confirmation moves forward. For one that is already
        // captured, released or refunded a repeated event must change nothing: moving a refunded
        // order back to Paid would let auto-release pay the seller for money already returned
        if (payment == null || payment.Status != PaymentStatus.Pending)
            return false;

        var now = _time.GetUtcNow().UtcDateTime;
        payment.Status = PaymentStatus.Captured;
        payment.CapturedAt = now;
        payment.Order.Status = OrderStatus.Paid;
        payment.Order.PaidAt = now;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task SellerAccountReadyAsync(string stripeAccountId)
    {
        var profile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.StripeAccountId == stripeAccountId);

        if (profile != null && !profile.StripeOnboardingComplete)
        {
            profile.StripeOnboardingComplete = true;
            await _context.SaveChangesAsync();
        }
    }
}
