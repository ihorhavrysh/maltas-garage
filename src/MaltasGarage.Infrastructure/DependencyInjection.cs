using MaltasGarage.Application.Common.Interfaces;
using MaltasGarage.Application.Common.Models;
using MaltasGarage.Infrastructure.Data;
using MaltasGarage.Infrastructure.Data.Seed;
using MaltasGarage.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MaltasGarage.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Settings: bound once, validated on startup so a missing value fails fast with a clear message
        services.AddValidatedOptions<AppSettings>(configuration, AppSettings.SectionName);
        services.AddValidatedOptions<StripeSettings>(configuration, StripeSettings.SectionName);
        services.AddValidatedOptions<StorageSettings>(configuration, StorageSettings.SectionName);
        services.AddValidatedOptions<EmailSettings>(configuration, EmailSettings.SectionName);
        services.AddValidatedOptions<DemoSettings>(configuration, DemoSettings.SectionName);

        // Database
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                b =>
                {
                    b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                    // A serverless Azure SQL database auto-pauses when idle and needs up to
                    // about a minute to resume; transient errors (e.g. 40613) are retried with
                    // back-off, and commands get enough time to wait for the resume.
                    b.EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(20), errorNumbersToAdd: null);
                    b.CommandTimeout(60);
                }));

        services.AddScoped<IApplicationDbContext>(provider =>
            provider.GetRequiredService<ApplicationDbContext>());

        // Image storage
        services.AddSingleton<LocalUploadStorage>();
        var storage = configuration.GetSection(StorageSettings.SectionName).Get<StorageSettings>() ?? new StorageSettings();
        if (storage.Provider == StorageProvider.AzureBlob)
            services.AddScoped<IImageService, AzureBlobImageService>();
        else
            services.AddScoped<IImageService, LocalImageService>();

        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IBiddingService, BiddingService>();
        services.AddScoped<IMessagingService, MessagingService>();
        services.AddScoped<IDisputeService, DisputeService>();
        services.AddScoped<IPriceOfferService, PriceOfferService>();
        services.AddScoped<IBundleOfferService, BundleOfferService>();
        services.AddScoped<IListingLifecycleService, ListingLifecycleService>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IEmailNotificationService, EmailNotificationService>();
        services.AddTransient<IEmailSender<IdentityUser>, IdentityEmailSender>();
        services.AddSingleton<EmailTemplate>();

        // Demo
        services.AddScoped<DemoDataSeeder>();
        services.AddScoped<DemoResetService>();

        // Payments
        services.AddScoped<IPaymentService, StripePaymentService>();

        // Email: every sender (notifications, Identity, contact form, admin) goes through IEmailService
        var email = configuration.GetSection(EmailSettings.SectionName).Get<EmailSettings>() ?? new EmailSettings();
        if (email.Provider == EmailProvider.Resend)
        {
            services.AddHttpClient(ResendEmailService.HttpClientName, client =>
            {
                client.BaseAddress = new Uri("https://api.resend.com/");
                client.Timeout = TimeSpan.FromSeconds(10);
            });
            services.AddScoped<IEmailService, ResendEmailService>();
        }
        else
        {
            services.AddScoped<IEmailService, LoggingEmailService>();
        }

        return services;
    }

    private static void AddValidatedOptions<T>(this IServiceCollection services, IConfiguration configuration, string sectionName)
        where T : class
    {
        services.AddOptions<T>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }
}
