using MaltasGarage.Domain.Common;

namespace MaltasGarage.Domain.Entities;

public class Category : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? IconClass { get; set; } // Bootstrap icon class, e.g., "bi-laptop"
    public Guid? ParentId { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public Category? Parent { get; set; }
    public ICollection<Category> Children { get; set; } = new List<Category>();
    public ICollection<Listing> Listings { get; set; } = new List<Listing>();
}
