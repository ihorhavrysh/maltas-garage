namespace MaltasGarage.Web.Services;

/// <summary>
/// Response headers that tell the browser what this site may do. The CSP lists every outside
/// origin the pages use: Stripe (card form, 3-D Secure frames, API), Google Analytics and Google
/// Fonts. Scripts still allow inline code: the pages use inline handlers and script blocks, which
/// nonces cannot cover; what the policy does stop is script from any other host, plugins,
/// framing by other sites and forms posting elsewhere.
/// </summary>
public static class SecurityHeaders
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' https://js.stripe.com https://www.googletagmanager.com; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' data: https://fonts.gstatic.com; " +
        "img-src 'self' data: blob: https:; " +
        "connect-src 'self' https://api.stripe.com https://*.google-analytics.com https://*.analytics.google.com https://www.googletagmanager.com; " +
        "frame-src https://js.stripe.com https://hooks.stripe.com; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers["Content-Security-Policy"] = ContentSecurityPolicy;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(self \"https://js.stripe.com\")";
                return Task.CompletedTask;
            });
            await next();
        });
}
