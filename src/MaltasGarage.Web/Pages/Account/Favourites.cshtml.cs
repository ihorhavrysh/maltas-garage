using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Pages.Account;

[Authorize]
public class FavouritesModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public FavouritesModel(ApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public UserProfile? Profile { get; set; }
    public List<Favourite> Favourites { get; set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (_currentUser.UserId == null)
            return RedirectToPage("/Account/Login", new { area = "Identity" });

        Profile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (Profile == null)
            return NotFound();

        Favourites = await _context.Favourites
            .Include(f => f.Listing)
                .ThenInclude(l => l.Images)
            .Include(f => f.Listing)
                .ThenInclude(l => l.Category)
            .Include(f => f.Listing)
                .ThenInclude(l => l.Bids)
            .Where(f => f.UserId == Profile.Id)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync();

        return Page();
    }
}
