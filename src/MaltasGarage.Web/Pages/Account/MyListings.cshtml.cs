using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Exceptions;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Pages.Account;

[Authorize]
public class MyListingsModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IListingRemovalService _removal;

    public MyListingsModel(ApplicationDbContext context, ICurrentUserService currentUser, IListingRemovalService removal)
    {
        _context = context;
        _currentUser = currentUser;
        _removal = removal;
    }

    public UserProfile? Profile { get; set; }
    public List<Listing> ActiveListings { get; set; } = [];
    public List<Listing> AuctionListings { get; set; } = [];
    public List<Listing> SoldListings { get; set; } = [];
    public List<Listing> OtherListings { get; set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (_currentUser.UserId == null)
            return RedirectToPage("/Account/Login", new { area = "Identity" });

        Profile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (Profile == null)
            return NotFound();

        var all = await _context.Listings
            .Include(l => l.Images)
            .Include(l => l.Category)
            .Include(l => l.Bids)
            .Where(l => l.SellerId == Profile.Id)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();

        ActiveListings  = all.Where(l => l.Status == ListingStatus.Active).ToList();
        AuctionListings = all.Where(l => l.Status == ListingStatus.AuctionPhase).ToList();
        SoldListings    = all.Where(l => l.Status == ListingStatus.Sold).ToList();
        OtherListings   = all.Where(l => l.Status is ListingStatus.Expired
                                          or ListingStatus.Cancelled).ToList();

        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        if (_currentUser.UserId == null)
            return RedirectToPage("/Account/Login", new { area = "Identity" });

        var profile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (profile == null) return NotFound();

        try
        {
            await _removal.DeleteAsync(id, profile.Id);
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToPage();
        }

        TempData["Success"] = "Listing deleted successfully.";
        return RedirectToPage();
    }
}
