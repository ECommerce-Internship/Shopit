using Shopit.Application.Common;
using Shopit.Application.Products.DTOs;

namespace Shopit.Application.Products;

public interface IProductService
{
    Task<PaginatedResult<ProductResponse>> GetAllAsync(ProductQueryParameters queryParameters);

    Task<ProductResponse> GetByIdAsync(int id);

    Task<ProductResponse> CreateAsync(CreateProductRequest request);

    Task<ProductResponse> UpdateAsync(int id, UpdateProductRequest request);

    Task DeleteAsync(int id);

    Task<ImportResultDto> ImportAsync(Stream fileStream, CancellationToken cancellationToken = default);
}