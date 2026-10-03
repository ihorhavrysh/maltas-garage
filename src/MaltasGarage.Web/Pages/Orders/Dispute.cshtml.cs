using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Domain.Entities;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Web.Pages.Orders;

[Authorize]
public class DisputeModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IDisputeService _disputeService;
    private readonly IImageService _imageService;

    public DisputeModel(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        IDisputeService disputeService,
        IImageService imageService)
    {
        _context = context;
        _currentUser = currentUser;
        _disputeService = disputeService;
        _imageService = imageService;
    }

    public Guid OrderId { get; set; }
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid orderId)
    {
        var userProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (userProfile == null)
            return RedirectToPage("/Index");

        var order = await _context.Orders
            .Include(o => o.Dispute)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null) return NotFound();
        if (order.BuyerId != userProfile.Id) return Forbid();
        if (order.Dispute != null && order.Dispute.Status != DisputeStatus.Withdrawn) return RedirectToPage("DisputeView", new { orderId });
        if (order.Status != OrderStatus.Paid && order.Status != OrderStatus.Shipped)
            return RedirectToPage("Details", new { orderId });

        OrderId = orderId;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(
        Guid orderId,
        string reason,
        string? description,
        List<IFormFile>? attachments)
    {
        var userProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId);

        if (userProfile == null) return Forbid();

        if (!Enum.TryParse<DisputeReason>(reason, out var disputeReason))
        {
            OrderId = orderId;
            ErrorMessage = "Please select a valid reason.";
            return Page();
        }

        try
        {
            var dispute = await _disputeService.OpenDisputeAsync(orderId, userProfile.Id, disputeReason, description);

            if (attachments != null && attachments.Count > 0)
            {
                foreach (var file in attachments.Take(5))
                {
                    if (file.Length == 0 || file.Length > 5 * 1024 * 1024) continue;

                    using var stream = file.OpenReadStream();
                    var result = await _imageService.UploadAsync(stream, file.FileName, DisputeFileModel.Folder);

                    if (result.Success)
                    {
                        _context.DisputeAttachments.Add(new DisputeAttachment
                        {
                            DisputeId = dispute.Id,
                            Url = result.Url!,
                            FileName = file.FileName
                        });
                    }
                }

                await _context.SaveChangesAsync();
            }

            TempData["Success"] = "Your dispute has been submitted. Our team will review it shortly.";
            return RedirectToPage("DisputeView", new { orderId });
        }
        catch (InvalidOperationException ex)
        {
            OrderId = orderId;
            ErrorMessage = ex.Message;
            return Page();
        }
    }
}
