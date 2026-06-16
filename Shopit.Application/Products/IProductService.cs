using Microsoft.AspNetCore.Http;
using Shopit.Application.Common;
using Shopit.Application.Interfaces;
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

    Task<string> UploadImageAsync(int productId, IFormFile file, IBlobStorageService blobStorageService, string containerName);

    Task DeleteImageAsync(int productId, IBlobStorageService blobStorageService, string containerName);
}