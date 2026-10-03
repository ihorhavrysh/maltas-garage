using MaltasGarage.Web.Services;
using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Infrastructure.Data;
using MaltasGarage.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Pages.Orders;

/// <summary>
/// Serves dispute evidence. The photos can show addresses, faces or documents, so unlike listing
/// photos they are not public: only the buyer, the seller and staff get them. The static files
/// middleware refuses /uploads/disputes/ (Program.cs), so this page is the only way in.
/// </summary>
[Authorize]
public class DisputeFileModel : PageModel
{
    public const string Folder = "disputes";

    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly LocalUploadStorage _storage;

    public DisputeFileModel(ApplicationDbContext context, ICurrentUserService currentUser, LocalUploadStorage storage)
    {
        _context = context;
        _currentUser = currentUser;
        _storage = storage;
    }

    public async Task<IActionResult> OnGetAsync(Guid attachmentId)
    {
        var attachment = await _context.DisputeAttachments
            .Where(a => a.Id == attachmentId)
            .Select(a => new { a.Url, a.Dispute.Order.BuyerId, a.Dispute.Order.SellerId })
            .FirstOrDefaultAsync();
        if (attachment == null)
            return NotFound();

        var profileId = await _context.UserProfiles
            .Where(p => p.UserId == _currentUser.UserId)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync();

        var allowed = profileId == attachment.BuyerId || profileId == attachment.SellerId ||
                      User.IsStaff();
        if (!allowed)
            return NotFound();   // not Forbid: do not confirm that the file exists

        var path = _storage.PathFromUrl(attachment.Url);
        if (path == null)
            return Redirect(attachment.Url);   // blob storage: the container is private, access is up to it

        if (!System.IO.File.Exists(path))
            return NotFound();

        Response.Headers.CacheControl = "private, max-age=3600";
        return PhysicalFile(path, "image/jpeg");
    }
}
