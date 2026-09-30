using MaltasGarage.Application.Common.Models;
using Microsoft.Extensions.Options;

namespace MaltasGarage.Infrastructure.Services;

/// <summary>The shared HTML layout of every email. Links point at App:BaseUrl.</summary>
public class EmailTemplate
{
    private readonly string _baseUrl;

    public EmailTemplate(IOptions<AppSettings> appSettings)
    {
        _baseUrl = appSettings.Value.BaseUrl.TrimEnd('/');
    }

    public string Build(string heading, string bodyHtml, string? buttonText = null, string? buttonUrl = null)
    {
        var button = buttonText != null && buttonUrl != null
            ? $"""<tr><td style="padding-top:24px;"><a href="{buttonUrl}" style="display:inline-block;background:#7CB342;color:#ffffff;text-decoration:none;padding:12px 28px;border-radius:6px;font-weight:bold;font-size:14px;">{buttonText}</a></td></tr>"""
            : "";

        return $"""
            <!DOCTYPE html>
            <html>
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
            <body style="margin:0;padding:0;background:#f0f2f5;font-family:Arial,Helvetica,sans-serif;">
              <table width="100%" cellpadding="0" cellspacing="0" style="background:#f0f2f5;padding:30px 0;">
                <tr><td align="center">
                  <table width="600" cellpadding="0" cellspacing="0" style="background:#ffffff;border-radius:10px;overflow:hidden;max-width:600px;width:100%;">
                    <tr>
                      <td style="background:#7CB342;padding:20px 32px;">
                        <span style="color:#ffffff;font-size:20px;font-weight:bold;letter-spacing:-0.3px;">Malta's Garage</span>
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:32px;">
                        <table width="100%" cellpadding="0" cellspacing="0">
                          <tr><td><h2 style="margin:0 0 16px;color:#1a1a2e;font-size:22px;font-weight:bold;">{heading}</h2></td></tr>
                          <tr><td style="color:#444444;font-size:15px;line-height:1.7;">{bodyHtml}</td></tr>
                          {button}
                        </table>
                      </td>
                    </tr>
                    <tr>
                      <td style="background:#f8f9fa;padding:16px 32px;border-top:1px solid #e9ecef;">
                        <p style="color:#999999;font-size:12px;margin:0;">
                          Malta's Garage - Malta's local marketplace for buying and selling second-hand items.<br>
                          <a href="{_baseUrl}/Account/Settings" style="color:#999999;">Manage notification preferences</a>
                        </p>
                      </td>
                    </tr>
                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;
    }
}
