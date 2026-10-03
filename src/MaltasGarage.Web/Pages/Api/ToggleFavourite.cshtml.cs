using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Pages.Api;

[Authorize]
public class ToggleFavouriteModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public ToggleFavouriteModel(ApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<IActionResult> OnPostAsync([FromForm] Guid listingId)
    {
        var profile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (profile == null)
            return new JsonResult(new { success = false, error = "Not found" });

        var existing = await _context.Favourites
            .FirstOrDefaultAsync(f => f.UserId == profile.Id && f.ListingId == listingId);

        bool isFavourited;
        if (existing != null)
        {
            _context.Favourites.Remove(existing);
            isFavourited = false;
        }
        else if (!await _context.Listings.AnyAsync(l => l.Id == listingId))
        {
            // An unknown id would otherwise fail on the foreign key with a 500
            return new JsonResult(new { success = false, error = "Listing not found" });
        }
        else
        {
            _context.Favourites.Add(new Favourite
            {
                Id = Guid.NewGuid(),
                UserId = profile.Id,
                ListingId = listingId,
                CreatedAt = DateTime.UtcNow
            });
            isFavourited = true;
        }

        await _context.SaveChangesAsync();
        return new JsonResult(new { success = true, isFavourited });
    }
}
