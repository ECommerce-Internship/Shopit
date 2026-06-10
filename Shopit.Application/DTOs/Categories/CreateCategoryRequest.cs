namespace Shopit.Application.DTOs.Categories;

public class CreateCategoryRequest
{
    public string Name { get; set; } = string.Empty;
    public int? ParentCategoryId { get; set; }
}