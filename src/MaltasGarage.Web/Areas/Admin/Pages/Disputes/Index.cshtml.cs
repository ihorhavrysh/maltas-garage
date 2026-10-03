using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Areas.Admin.Pages.Disputes;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly IDisputeService _disputeService;

    public IndexModel(ApplicationDbContext context, IDisputeService disputeService)
    {
        _context = context;
        _disputeService = disputeService;
    }

    public List<Dispute> Disputes { get; set; } = new();
    public string Filter { get; set; } = "open";

    public async Task OnGetAsync(string filter = "open")
    {
        ViewData["ActivePage"] = "Disputes";
        Filter = filter;

        var query = _context.Disputes
            .Include(d => d.Order).ThenInclude(o => o.Listing)
            .Include(d => d.Order).ThenInclude(o => o.Buyer)
            .Include(d => d.Order).ThenInclude(o => o.Seller)
            .Include(d => d.OpenedBy)
            .AsQueryable();

        query = filter switch
        {
            "resolved" => query.Where(d => d.Status == DisputeStatus.Resolved || d.Status == DisputeStatus.Withdrawn),
            "all"      => query,
            _          => query.Where(d => d.Status == DisputeStatus.Open || d.Status == DisputeStatus.UnderReview)
        };

        Disputes = await query.OrderByDescending(d => d.CreatedAt).ToListAsync();
    }

    public async Task<IActionResult> OnPostMarkUnderReviewAsync(Guid disputeId)
    {
        var dispute = await _context.Disputes.FirstOrDefaultAsync(d => d.Id == disputeId);
        if (dispute != null)
            await _disputeService.MarkUnderReviewAsync(dispute.OrderId);

        return RedirectToPage(new { filter = "open" });
    }

    public async Task<IActionResult> OnPostResolveAsync(Guid disputeId, string resolution, string? adminNotes, decimal? partialRefundAmount)
    {
        if (!Enum.TryParse<DisputeResolution>(resolution, out var res))
            return RedirectToPage(new { filter = "open" });

        try
        {
            await _disputeService.ResolveDisputeAsync(disputeId, res, adminNotes, partialRefundAmount);
            TempData["Success"] = "Dispute resolved.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Failed to resolve dispute: {ex.Message}";
        }

        return RedirectToPage(new { filter = "open" });
    }
}
