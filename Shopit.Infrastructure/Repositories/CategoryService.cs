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
    private readonly ICategoryRepository _repository;

public CategoryService(ICategoryRepository repository)
{
    _repository = repository;
}

   public async Task<List<CategoryResponse>> GetAllAsync()
    {
        var categories = await _repository.GetAllRootAsync();
        Log.Information("Retrieved {Count} root categories", categories.Count);
        return categories.Select(MapToResponse).ToList();
    }


   public async Task<CategoryResponse> GetByIdAsync(int id)
    {
        var category = await _repository.GetByIdAsync(id);

        if (category is null)
        {
            Log.Warning("Category not found: {CategoryId}", id);
            throw new NotFoundException($"Category with ID {id} was not found.");
        }

        return MapToResponse(category);
    }

    public async Task<CategoryResponse> CreateAsync(CreateCategoryRequest request)
    {
        var existing = await _repository.GetByNameAsync(request.Name);

        if (existing is not null)
        {
            Log.Warning("Duplicate category name attempted: {Name}", request.Name);
            throw new ConflictException($"A category with the name '{request.Name}' already exists.");
        }
        if (request.ParentCategoryId.HasValue)
        {
            var parentExists = await _repository.GetByIdAsync(request.ParentCategoryId.Value);

            if (parentExists is null)
                throw new NotFoundException($"Parent category with ID {request.ParentCategoryId.Value} was not found.");
        }
        var category = new Category
        {
            Name = request.Name,
            Slug = request.Name.ToLower().Replace(" ", "-"),
            ParentCategoryId = request.ParentCategoryId
        };

        await _repository.AddAsync(category);
        await _repository.SaveChangesAsync();

        Log.Information("Category created: {CategoryId} - {Name}", category.Id, category.Name);

        return MapToResponse(category);
    }

    public async Task<CategoryResponse> UpdateAsync(int id, UpdateCategoryRequest request)
    {
        var category = await _repository.GetByIdAsync(id);
        if (category is null)
            throw new NotFoundException($"Category with ID {id} was not found.");

        var nameExists = await _repository.GetByNameAsync(request.Name);
        if (nameExists is not null && nameExists.Id != id)
            throw new ConflictException($"A category with the name '{request.Name}' already exists.");

        if (request.ParentCategoryId.HasValue && request.ParentCategoryId.Value == id)
            throw new ConflictException("A category cannot be its own parent.");

        if (request.ParentCategoryId.HasValue)
        {
            var parentExists = await _repository.GetByIdAsync(request.ParentCategoryId.Value);
            if (parentExists is null)
                throw new NotFoundException($"Parent category with ID {request.ParentCategoryId.Value} was not found.");
        }

        category.Name = request.Name;
        category.Slug = request.Name.ToLower().Replace(" ", "-");
        category.ParentCategoryId = request.ParentCategoryId;

        await _repository.SaveChangesAsync();
        Log.Information("Category updated: {CategoryId}", id);
        return MapToResponse(category);
    }


     public async Task DeleteAsync(int id)
    {
        var category = await _repository.GetByIdAsync(id);
        if (category is null)
            throw new NotFoundException($"Category with ID {id} was not found.");

        var productCount = await _repository.GetProductCountAsync(id);
        if (productCount > 0)
        {
            Log.Warning("Delete blocked: Category {CategoryId} has {Count} linked products", id, productCount);
            throw new ConflictException($"Cannot delete category '{category.Name}' because it has {productCount} linked product(s). Remove the products first.");
        }

        await _repository.DeleteAsync(category);
        await _repository.SaveChangesAsync();
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