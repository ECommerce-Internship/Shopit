namespace Shopit.Application.DTOs.Categories;

public class CategoryResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int? ParentCategoryId { get; set; }
    public int SubcategoryCount { get; set; }
    public List<CategoryResponse> Subcategories { get; set; } = new();
}