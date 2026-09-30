using System.ComponentModel.DataAnnotations;
using MaltasGarage.Application.Common.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace MaltasGarage.Web.Pages;

[EnableRateLimiting("contact")]
public class ContactModel : PageModel
{
    private readonly IEmailNotificationService _emailNotifications;

    public ContactModel(IEmailNotificationService emailNotifications)
        => _emailNotifications = emailNotifications;

    [BindProperty]
    public ContactInput Input { get; set; } = new();

    public class ContactInput
    {
        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required, EmailAddress, MaxLength(200)]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Subject { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? ListingOrOrderRef { get; set; }

        [Required, MinLength(20, ErrorMessage = "Please provide more detail (at least 20 characters)."), MaxLength(3000)]
        public string Message { get; set; } = string.Empty;
    }

    // Honeypot field - must stay empty; bots fill it in
    [BindProperty] public string? Fax { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        // If honeypot is filled, silently discard (bot detected)
        if (!string.IsNullOrEmpty(Fax))
        {
            TempData["Success"] = "Thank you for your message! We'll get back to you within 1 business day.";
            return RedirectToPage();
        }

        if (!ModelState.IsValid)
            return Page();

        var fullMessage = string.IsNullOrEmpty(Input.ListingOrOrderRef)
            ? Input.Message
            : $"Ref: {Input.ListingOrOrderRef}\n\n{Input.Message}";

        await _emailNotifications.SendContactFormAsync(Input.Name, Input.Email, Input.Subject, fullMessage);

        TempData["Success"] = "Thank you for your message! We'll get back to you within 1 business day.";
        return RedirectToPage();
    }
}
