using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Infrastructure.Services;

public class ReviewService : IReviewService
{
    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _time;

    public ReviewService(ApplicationDbContext context, TimeProvider time)
    {
        _context = context;
        _time = time;
    }

    public async Task LeaveReviewAsync(Guid orderId, Guid fromUserId, int rating, string? comment)
    {
        if (rating is < 1 or > 5)
            throw new InvalidOperationException("Please choose a rating from 1 to 5.");

        var order = await _context.Orders
            .Include(o => o.Reviews)
            .FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw new InvalidOperationException("Order not found.");

        if (order.BuyerId != fromUserId && order.SellerId != fromUserId)
            throw new InvalidOperationException("Only the buyer and the seller can review this order.");

        if (order.Status != OrderStatus.Completed)
            throw new InvalidOperationException("Reviews can only be left after the order is completed.");

        if (order.Reviews.Any(r => r.FromUserId == fromUserId))
            throw new InvalidOperationException("You have already reviewed this order.");

        var toUserId = order.BuyerId == fromUserId ? order.SellerId : order.BuyerId;
        _context.Reviews.Add(new Review
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            FromUserId = fromUserId,
            ToUserId = toUserId,
            Rating = rating,
            Comment = comment,
            CreatedAt = _time.GetUtcNow().UtcDateTime
        });

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Double submit: the unique (OrderId, FromUserId) index stopped the second one
            throw new InvalidOperationException("You have already reviewed this order.");
        }

        // Recomputed from all reviews after the save, so the new one is included
        var ratings = await _context.Reviews
            .Where(r => r.ToUserId == toUserId)
            .Select(r => r.Rating)
            .ToListAsync();

        var user = await _context.UserProfiles.FindAsync(toUserId);
        if (user != null && ratings.Count > 0)
        {
            user.Rating = Math.Round((decimal)ratings.Average(), 1);
            user.TotalReviews = ratings.Count;
            await _context.SaveChangesAsync();
        }
    }
}
