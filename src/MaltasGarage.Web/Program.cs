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
    options.Password.RequiredLength = 8;
    // Used by the login page for every account except the shared demo ones (see LoginModel)
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
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

// A ban or a password change updates the security stamp; signed-in cookies are re-checked
// against it every 5 minutes (the default is 30), so a banned user is signed out quickly
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = TimeSpan.FromMinutes(5));

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdminRole", policy => policy.RequireRole("Admin"));
    options.AddPolicy("RequireAdminOrManagerRole", policy => policy.RequireRole(StaffRoles.Admin, StaffRoles.Manager));
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

    // Named policies are partitioned by client IP: each visitor has their own counter. A plain
    // AddFixedWindowLimiter would be one counter for the whole site, which one visitor can use up

    // Bid page: 5 requests/min per IP - prevents payment intent spam
    options.AddPolicy("bid", ctx => RateLimitPartition.GetFixedWindowLimiter(
        $"bid:{ctx.Connection.RemoteIpAddress}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));

    // Contact form: 3 submissions per 10 min per IP (the GET that shows the form is not limited)
    options.AddPolicy("contact", ctx => HttpMethods.IsPost(ctx.Request.Method)
        ? RateLimitPartition.GetFixedWindowLimiter($"contact:{ctx.Connection.RemoteIpAddress}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 3,
            Window = TimeSpan.FromMinutes(10),
            QueueLimit = 0
        })
        : RateLimitPartition.GetNoLimiter("contact-get"));

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

        // Password reset emails: 5 per hour, so the form cannot be used to flood someone's inbox
        if (isPost && (path.StartsWith("/identity/account/forgotpassword") ||
                       path.StartsWith("/identity/account/resendemailconfirmation")))
        {
            return RateLimitPartition.GetFixedWindowLimiter($"reset:{ip}", _ => new()
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

        // Everything else: a generous ceiling per IP. A normal visitor never reaches it, but a
        // script hammering pages would otherwise burn the free tier's daily CPU quota
        if (path.StartsWith("/css/") || path.StartsWith("/js/") || path.StartsWith("/lib/") ||
            path.StartsWith("/images/") || path.StartsWith("/uploads/") || path == "/robots.txt")
            return RateLimitPartition.GetNoLimiter("static");

        return RateLimitPartition.GetFixedWindowLimiter($"pages:{ip}", _ => new()
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
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

// Apply pending EF Core migrations automatically on startup. A free database that has used its
// monthly allowance stays paused until the 1st: the app then starts anyway and every page
// explains it (Error page), instead of crashing and restarting in a loop that burns CPU quota.
// Migrations and seeding run on the next start after the database is back
var databaseReady = true;
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    try
    {
        // The migrations are written for SQL Server; the web tests run the app on SQLite
        if (db.Database.IsSqlServer())
            db.Database.Migrate();
        else
            db.Database.EnsureCreated();
    }
    catch (Exception ex) when (DatabaseErrors.IsFreeLimitReached(ex))
    {
        databaseReady = false;
        app.Logger.LogCritical("Database paused until {ResumesAt}: monthly free allowance used. Starting without migrations and seeding.",
            DatabaseErrors.ResumesAtUtc(DateTime.UtcNow));
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseSecurityHeaders();
app.UseStatusCodePagesWithReExecute("/Error", "?statusCode={0}");

if (!app.Environment.IsDevelopment())
    app.UseResponseCompression();
app.UseHttpsRedirection();

// Dispute evidence is not public: it is served by /Orders/DisputeFile after an access check
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments($"{LocalUploadStorage.RequestPath}/{MaltasGarage.Web.Pages.Orders.DisputeFileModel.Folder}"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    await next();
});

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

// robots.txt is generated so the sitemap URL follows App:BaseUrl. The demo asks crawlers to stay
// away: every crawl wakes the free database for at least an hour of its monthly allowance
app.MapGet("/robots.txt", (SiteInfo site) => Results.Text(
    demoEnabled
        ? "User-agent: *\nDisallow: /\n"
        : string.Join('\n',
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
app.MapPost("/api/stripe/webhook", async (HttpContext http, IPaymentEventHandler events,
    IPurchaseCompletionService purchases, IOptions<StripeSettings> stripe) =>
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
        // Repeated or late deliveries are expected; the handler ignores what no longer applies
        // A buyer who closes the tab after paying never comes back to the payment page; the order
        // (or bid) is then created from here. Whichever of the two runs second finds it done
        if (stripeEvent.Data.Object is PaymentIntent intent &&
            !await events.PaymentSucceededAsync(intent.Id) &&
            intent.Metadata?.ContainsKey(PaymentMetadata.Purpose) == true)
        {
            await purchases.CompleteAsync(new ConfirmedPayment(intent.Id, intent.AmountReceived / 100m, intent.Metadata));
        }
    }
    else if (stripeEvent.Type == "account.updated")
    {
        if (stripeEvent.Data.Object is Account { ChargesEnabled: true, PayoutsEnabled: true } account)
            await events.SellerAccountReadyAsync(account.Id);
    }

    return Results.Ok();
});

// Seed database
if (databaseReady)
{
    using var scope = app.Services.CreateScope();
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

// Lets the web tests start the app with WebApplicationFactory<Program>
public partial class Program;
