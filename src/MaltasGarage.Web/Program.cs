using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Application.Common.Models;
using MaltasGarage.Domain.Enums;
using MaltasGarage.Infrastructure;
using MaltasGarage.Infrastructure.Data;
using MaltasGarage.Infrastructure.Data.Seed;
using MaltasGarage.Infrastructure.Services;
using MaltasGarage.Web.Filters;
using MaltasGarage.Web.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;
using System.Globalization;
using System.IO.Compression;
using System.Threading.RateLimiting;

// Configuration sources: appsettings.json, appsettings.{Environment}.json, User Secrets
// (Development) and environment variables (App Service application settings in Azure).
var builder = WebApplication.CreateBuilder(args);
var demoEnabled = builder.Configuration.GetSection(DemoSettings.SectionName).Get<DemoSettings>()?.Enabled == true;

// Response compression (Brotli + Gzip) - production only
// In development, Browser Link/Refresh can't inject scripts into compressed responses
if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddResponseCompression(options =>
    {
        options.EnableForHttps = true;
        options.Providers.Add<BrotliCompressionProvider>();
        options.Providers.Add<GzipCompressionProvider>();
    });
    builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
        options.Level = CompressionLevel.Fastest);
    builder.Services.Configure<GzipCompressionProviderOptions>(options =>
        options.Level = CompressionLevel.Fastest);
}

// Add services to the container.
builder.Services.AddRazorPages();

// Add Infrastructure (Database, etc.)
builder.Services.AddInfrastructure(builder.Configuration);

// Add HttpContext and CurrentUser service
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddSingleton<SiteInfo>();

// One catch-up sweep for all time-based transitions (see IListingLifecycleService)
builder.Services.AddHostedService<MarketplaceSweepService>();

// Add Identity
builder.Services.AddDefaultIdentity<IdentityUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 6;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddRazorPages()
    .AddRazorPagesOptions(options =>
    {
        // Whole admin area requires Admin or Manager
        options.Conventions.AuthorizeAreaFolder("Admin", "/", "RequireAdminOrManagerRole");
        // Staff page is Admin-only
        options.Conventions.AuthorizeAreaFolder("Admin", "/Staff", "RequireAdminRole");
        // Sitemap route
        options.Conventions.AddPageRoute("/Sitemap", "/sitemap.xml");
    })
    .AddMvcOptions(options =>
    {
        // Public demo: protect the shared demo accounts and the shared Stripe test account
        if (demoEnabled)
            options.Filters.Add<DemoGuardPageFilter>();
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdminRole", policy => policy.RequireRole("Admin"));
    options.AddPolicy("RequireAdminOrManagerRole", policy => policy.RequireRole("Admin", "Manager"));
});

// HSTS: 1 year (non-development only). No IncludeSubDomains/Preload: the demo runs on a
// shared *.azurewebsites.net host, where those directives are not ours to set.
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
});

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Bid page: 5 requests/min per IP - prevents payment intent spam
    options.AddFixedWindowLimiter("bid", o =>
    {
        o.PermitLimit = 5;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueLimit = 0;
    });

    // Contact form: 3 submissions per 10 min per IP
    options.AddFixedWindowLimiter("contact", o =>
    {
        o.PermitLimit = 3;
        o.Window = TimeSpan.FromMinutes(10);
        o.QueueLimit = 0;
    });

    // Path-based limits per IP for the endpoints a public demo attracts abuse on
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var path = ctx.Request.Path.Value?.ToLower() ?? "";
        var isPost = HttpMethods.IsPost(ctx.Request.Method);

        // Account creation: 5 per hour
        if (isPost && path.StartsWith("/identity/account/register"))
        {
            return RateLimitPartition.GetFixedWindowLimiter($"register:{ip}", _ => new()
            {
                PermitLimit = 5,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0
            });
        }

        // Anything that stores images or listings: 30 submissions per hour
        if (isPost && (path.StartsWith("/listings/create") ||
                       path.StartsWith("/listings/edit") ||
                       path.StartsWith("/account/profile") ||
                       path.StartsWith("/orders/dispute")))
        {
            return RateLimitPartition.GetFixedWindowLimiter($"uploads:{ip}", _ => new()
            {
                PermitLimit = 30,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0
            });
        }

        // Login & Register pages: 10 attempts/min
        if (path.StartsWith("/identity/account/login") ||
            path.StartsWith("/identity/account/register"))
        {
            return RateLimitPartition.GetFixedWindowLimiter($"auth:{ip}", _ => new()
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
        }

        return RateLimitPartition.GetNoLimiter("nolimit");
    });
});

// Behind the App Service front end the client address arrives in X-Forwarded-For.
// Without this every visitor would share the proxy's IP and one rate-limit bucket.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

// English (Malta uses British conventions) everywhere, whatever culture the server OS has:
// request threads via request localization, background work via the default thread culture
var appCulture = new CultureInfo("en-GB");
CultureInfo.DefaultThreadCurrentCulture = appCulture;
CultureInfo.DefaultThreadCurrentUICulture = appCulture;
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(appCulture),
    SupportedCultures = new[] { appCulture },
    SupportedUICultures = new[] { appCulture },
    RequestCultureProviders = new List<IRequestCultureProvider>()
});

// Apply pending EF Core migrations automatically on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Error", "?statusCode={0}");

if (!app.Environment.IsDevelopment())
    app.UseResponseCompression();
app.UseHttpsRedirection();
app.UseStaticFiles(new StaticFileOptions
{
    // Only fingerprinted URLs (asp-append-version adds ?v=hash) may be cached forever;
    // anything else could change on the next deploy
    OnPrepareResponse = ctx =>
        ctx.Context.Response.Headers.Append("Cache-Control",
            ctx.Context.Request.Query.ContainsKey("v")
                ? "public, max-age=31536000, immutable"
                : "public, max-age=86400")
});

// Uploads stored outside wwwroot (App Service: the persistent /home volume) get their own
// file provider; their names are unique, so they can be cached for a long time
var uploadStorage = app.Services.GetRequiredService<LocalUploadStorage>();
if (!uploadStorage.IsInsideWebRoot)
{
    Directory.CreateDirectory(uploadStorage.RootPath);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(uploadStorage.RootPath),
        RequestPath = LocalUploadStorage.RequestPath,
        OnPrepareResponse = ctx => ctx.Context.Response.Headers.Append("Cache-Control", "public, max-age=604800")
    });
}

app.UseRouting();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

// robots.txt is generated so the sitemap URL follows App:BaseUrl
app.MapGet("/robots.txt", (SiteInfo site) => Results.Text(
    string.Join('\n',
        "User-agent: *",
        "Allow: /",
        "Disallow: /Admin/",
        "Disallow: /Account/",
        "Disallow: /Orders/",
        "Disallow: /Messages/",
        "Disallow: /Checkout",
        "Disallow: /Bid",
        "Disallow: /Identity/",
        $"Sitemap: {site.Absolute("/sitemap.xml")}") + "\n",
    "text/plain"));

// Stripe Webhook endpoint
app.MapPost("/api/stripe/webhook", async (HttpContext http, ApplicationDbContext db, IOptions<StripeSettings> stripe) =>
{
    var json = await new StreamReader(http.Request.Body).ReadToEndAsync();
    var webhookSecret = stripe.Value.WebhookSecret;

    Event stripeEvent;
    try
    {
        stripeEvent = EventUtility.ConstructEvent(
            json,
            http.Request.Headers["Stripe-Signature"],
            webhookSecret,
            throwOnApiVersionMismatch: false);
    }
    catch (StripeException)
    {
        return Results.BadRequest();
    }

    if (stripeEvent.Type == "payment_intent.succeeded")
    {
        var intent = stripeEvent.Data.Object as PaymentIntent;
        if (intent == null) return Results.Ok();

        var payment = await db.Payments
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.StripePaymentIntentId == intent.Id);

        if (payment != null && payment.Status != PaymentStatus.Captured)
        {
            payment.Status = PaymentStatus.Captured;
            payment.CapturedAt = DateTime.UtcNow;
            payment.Order.Status = OrderStatus.Paid;
            payment.Order.PaidAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }
    else if (stripeEvent.Type == "account.updated")
    {
        var account = stripeEvent.Data.Object as Account;
        if (account == null) return Results.Ok();

        if (account.ChargesEnabled && account.PayoutsEnabled)
        {
            var profile = await db.UserProfiles
                .FirstOrDefaultAsync(p => p.StripeAccountId == account.Id);

            if (profile != null && !profile.StripeOnboardingComplete)
            {
                profile.StripeOnboardingComplete = true;
                await db.SaveChangesAsync();
            }
        }
    }

    return Results.Ok();
});

// Seed database
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await CategorySeeder.SeedCategoriesAsync(context);
    await AdminSeeder.SeedAdminAsync(scope.ServiceProvider);

    if (demoEnabled)
    {
        await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync();
        await scope.ServiceProvider.GetRequiredService<DemoResetService>().ResetIfDueAsync();
    }
}

app.Run();
