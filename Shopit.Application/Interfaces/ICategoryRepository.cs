using Shopit.Domain.Entities;

namespace Shopit.Application.Interfaces;

public interface ICategoryRepository
{
    Task<List<Category>> GetAllRootAsync();
    Task<Category?> GetByIdAsync(int id);
    Task<Category?> GetByNameAsync(string name);
    Task AddAsync(Category category);
    Task DeleteAsync(Category category);
    Task<int> GetProductCountAsync(int categoryId);
    Task SaveChangesAsync();
}