using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Pages.Account;

[Authorize]
public class ReviewsModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public ReviewsModel(ApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public UserProfile? Profile { get; set; }
    public List<ReviewViewModel> Reviews { get; set; } = new();

    public class ReviewViewModel
    {
        public int Rating { get; set; }
        public string? Comment { get; set; }
        public string FromUserName { get; set; } = string.Empty;
        public string? FromUserAvatarUrl { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (_currentUser.UserId == null)
            return RedirectToPage("/Account/Login", new { area = "Identity" });

        Profile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (Profile == null)
            return NotFound();

        Reviews = await _context.Reviews
            .Include(r => r.FromUser)
            .Where(r => r.ToUserId == Profile.Id)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ReviewViewModel
            {
                Rating = r.Rating,
                Comment = r.Comment,
                FromUserName = r.FromUser.DisplayName ?? "User",
                FromUserAvatarUrl = r.FromUser.AvatarUrl,
                CreatedAt = r.CreatedAt
            })
            .ToListAsync();

        return Page();
    }
}
