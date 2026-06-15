using Microsoft.EntityFrameworkCore;
using Shopit.Application.Interfaces;
using Shopit.Domain.Entities;
using Shopit.Infrastructure.Data;

namespace Shopit.Infrastructure.Repositories;

public class CategoryRepository : ICategoryRepository
{
    private readonly AppDbContext _context;

    public CategoryRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<Category>> GetAllRootAsync()
    {
        return await _context.Categories
            .Include(c => c.SubCategories)
            .Where(c => c.ParentCategoryId == null)
            .ToListAsync();
    }

    public async Task<Category?> GetByIdAsync(int id)
    {
        return await _context.Categories
            .Include(c => c.SubCategories)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<Category?> GetByNameAsync(string name)
    {
        return await _context.Categories
            .FirstOrDefaultAsync(c => c.Name.ToLower() == name.ToLower());
    }

    public async Task AddAsync(Category category)
    {
        await _context.Categories.AddAsync(category);
    }

    public async Task DeleteAsync(Category category)
    {
        _context.Categories.Remove(category);
        await Task.CompletedTask;
    }

    public async Task<int> GetProductCountAsync(int categoryId)
    {
        return await _context.Products
            .CountAsync(p => p.CategoryId == categoryId);
    }

    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }
}