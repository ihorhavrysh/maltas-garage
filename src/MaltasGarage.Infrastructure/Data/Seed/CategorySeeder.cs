using MaltasGarage.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Infrastructure.Data.Seed;

public static class CategorySeeder
{
    public static async Task SeedCategoriesAsync(ApplicationDbContext context)
    {
        var desired = new List<(string Slug, string Name, string IconClass, int SortOrder)>
        {
            ("home-kitchen",   "Home & Kitchen",   "bi-house",   1),
            ("electronics",    "Electronics",      "bi-laptop",  2),
            ("pets",           "Pets",             "ti ti-paw",  3),
            ("clothing",       "Clothing",         "bi-bag",     4),
            ("garden-outdoor", "Garden & Outdoor", "bi-tree",    5),
            ("kids-baby",      "Kids & Baby",      "bi-rocket",  6),
            ("sports-leisure", "Sports & Leisure", "bi-bicycle", 7),
            ("other",          "Other",            "bi-box",     8),
        };

        var existing = await context.Categories.ToListAsync();
        var desiredSlugs = desired.Select(d => d.Slug).ToHashSet();

        // Remove categories no longer in the list. One that still has listings stays (the FK is
        // Restrict, so removing it would stop the app from starting); move its listings first
        var inUse = await context.Listings.Select(l => l.CategoryId).Distinct().ToListAsync();
        var toRemove = existing.Where(c => !desiredSlugs.Contains(c.Slug) && !inUse.Contains(c.Id)).ToList();
        if (toRemove.Count > 0)
            context.Categories.RemoveRange(toRemove);

        foreach (var (slug, name, iconClass, sortOrder) in desired)
        {
            var category = existing.FirstOrDefault(c => c.Slug == slug);
            if (category == null)
            {
                context.Categories.Add(new Category
                {
                    Id        = Guid.NewGuid(),
                    Slug      = slug,
                    Name      = name,
                    IconClass = iconClass,
                    SortOrder = sortOrder
                });
            }
            else
            {
                category.Name      = name;
                category.IconClass = iconClass;
                category.SortOrder = sortOrder;
            }
        }

        await context.SaveChangesAsync();
    }
}
