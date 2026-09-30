using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Infrastructure.Services;

public class IdentityEmailSender : IEmailSender<IdentityUser>
{
    private readonly IEmailService _email;
    private readonly ApplicationDbContext _context;
    private readonly EmailTemplate _template;

    public IdentityEmailSender(IEmailService email, ApplicationDbContext context, EmailTemplate template)
    {
        _email = email;
        _context = context;
        _template = template;
    }

    private async Task<string> GetDisplayNameAsync(string userId)
    {
        var profile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId);
        return profile?.DisplayName ?? "there";
    }

    public async Task SendConfirmationLinkAsync(IdentityUser user, string email, string confirmationLink)
    {
        var name = await GetDisplayNameAsync(user.Id);
        var html = _template.Build(
            "Confirm your email address",
            $"Hi {name},<br><br>" +
            $"Please confirm your email address to secure your Malta's Garage account.<br><br>" +
            $"Confirming your email ensures you can reset your password if you ever lose access to your account. Without it, account recovery won't be possible.<br><br>" +
            $"This link expires in <strong>24 hours</strong>. If you didn't create an account on Malta's Garage, you can safely ignore this email.",
            "Confirm Email", confirmationLink);
        await _email.SendAsync(email, name, "Confirm your Malta's Garage email address", html);
    }

    public async Task SendPasswordResetLinkAsync(IdentityUser user, string email, string resetLink)
    {
        var name = await GetDisplayNameAsync(user.Id);
        var html = _template.Build(
            "Reset your password",
            $"Hi {name},<br><br>You requested a password reset for your Malta's Garage account. Click the button below to set a new password.<br><br>If you didn't request this, you can safely ignore this email - your password won't change.",
            "Reset Password", resetLink);
        await _email.SendAsync(email, name, "Reset your Malta's Garage password", html);
    }

    public async Task SendPasswordResetCodeAsync(IdentityUser user, string email, string resetCode)
    {
        var name = await GetDisplayNameAsync(user.Id);
        var html = _template.Build(
            "Your password reset code",
            $"Hi {name},<br><br>Your Malta's Garage password reset code is:<br><br><div style=\"text-align:center;padding:20px;\"><strong style=\"font-size:28px;letter-spacing:6px;color:#1a1a2e;\">{resetCode}</strong></div><br>This code expires in 15 minutes. If you didn't request a reset, please ignore this email.");
        await _email.SendAsync(email, name, "Your Malta's Garage password reset code", html);
    }
}
