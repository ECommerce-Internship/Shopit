using Microsoft.EntityFrameworkCore;
using Shopit.Application.DTOs.Categories;
using Shopit.Application.Interfaces;
using Shopit.Domain.Entities;
using Shopit.Domain.Exceptions;
using Shopit.Infrastructure.Data;
using Serilog;

namespace Shopit.Infrastructure.Repositories;

public class CategoryService : ICategoryService
{
    private readonly AppDbContext _context;

    public CategoryService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<CategoryResponse>> GetAllAsync()
    {
        var categories = await _context.Categories
            .Include(c => c.SubCategories)
            .ToListAsync();

        Log.Information("Retrieved {Count} categories", categories.Count);

        return categories.Select(MapToResponse).ToList();
    }

    public async Task<CategoryResponse> GetByIdAsync(int id)
    {
        var category = await _context.Categories
            .Include(c => c.SubCategories)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (category is null)
        {
            Log.Warning("Category not found: {CategoryId}", id);
            throw new NotFoundException($"Category with ID {id} was not found.");
        }

        return MapToResponse(category);
    }

    public async Task<CategoryResponse> CreateAsync(CreateCategoryRequest request)
    {
        var exists = await _context.Categories
            .AnyAsync(c => c.Name.ToLower() == request.Name.ToLower());

        if (exists)
        {
            Log.Warning("Duplicate category name attempted: {Name}", request.Name);
            throw new ConflictException($"A category with the name '{request.Name}' already exists.");
        }

        var category = new Category
        {
            Name = request.Name,
            Slug = request.Name.ToLower().Replace(" ", "-"),
            ParentCategoryId = request.ParentCategoryId
        };

        _context.Categories.Add(category);
        await _context.SaveChangesAsync();

        Log.Information("Category created: {CategoryId} - {Name}", category.Id, category.Name);

        return MapToResponse(category);
    }

    public async Task<CategoryResponse> UpdateAsync(int id, UpdateCategoryRequest request)
    {
        var category = await _context.Categories
            .Include(c => c.SubCategories)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (category is null)
            throw new NotFoundException($"Category with ID {id} was not found.");
        // Duplicate name check — same as CreateAsync
    var nameExists = await _context.Categories
        .AnyAsync(c => c.Name.ToLower() == request.Name.ToLower() && c.Id != id);

    if (nameExists)
        throw new ConflictException($"A category with the name '{request.Name}' already exists.");
     // Guard against self-referencing parent
    if (request.ParentCategoryId.HasValue && request.ParentCategoryId.Value == id)
        throw new ConflictException("A category cannot be its own parent.");

    category.Name = request.Name;
    category.Slug = request.Name.ToLower().Replace(" ", "-");
    category.ParentCategoryId = request.ParentCategoryId;

    await _context.SaveChangesAsync();

    Log.Information("Category updated: {CategoryId}", id);

    return MapToResponse(category);
    }

    public async Task DeleteAsync(int id)
    {
        var category = await _context.Categories
            .Include(c => c.Products)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (category is null)
            throw new NotFoundException($"Category with ID {id} was not found.");

        if (category.Products.Any())
        {
            Log.Warning("Delete blocked: Category {CategoryId} has {Count} linked products", 
                id, category.Products.Count);
            throw new ConflictException(
                $"Cannot delete category '{category.Name}' because it has {category.Products.Count} linked product(s).");
        }

        _context.Categories.Remove(category);
        await _context.SaveChangesAsync();

        Log.Information("Category deleted: {CategoryId}", id);
    }

    private static CategoryResponse MapToResponse(Category category) => new()
    {
        Id = category.Id,
        Name = category.Name,
        Slug = category.Slug,
        ParentCategoryId = category.ParentCategoryId,
        SubcategoryCount = category.SubCategories?.Count ?? 0,
        Subcategories = category.SubCategories?.Select(MapToResponse).ToList() ?? new()
    };
}