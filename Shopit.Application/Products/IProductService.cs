using Microsoft.AspNetCore.Http;
using Shopit.Application.AI;
using Shopit.Application.Common;
using Shopit.Application.Interfaces;
using Shopit.Application.Products.DTOs;

namespace Shopit.Application.Products;

public interface IProductService
{
    Task<PaginatedResult<ProductResponse>> GetAllAsync(ProductQueryParameters queryParameters);

    Task<ProductResponse> GetByIdAsync(int id);

    Task<ProductResponse> CreateAsync(CreateProductRequest request, int userId, bool isAdmin);

    Task<ProductResponse> UpdateAsync(int id, UpdateProductRequest request, int userId, bool isAdmin);

    Task DeleteAsync(int id, int userId, bool isAdmin);

    Task<ImportResultDto> ImportAsync(Stream fileStream, CancellationToken cancellationToken = default);

    Task<string> UploadImageAsync(int productId, IFormFile file, IBlobStorageService blobStorageService, string containerName, int userId, bool isAdmin);

    Task DeleteImageAsync(int productId, IBlobStorageService blobStorageService, string containerName, int userId, bool isAdmin);
    Task<IEnumerable<ProductResponse>> SemanticSearchAsync(string query, int take = 10);

    Task<int> BackfillEmbeddingsAsync(CancellationToken ct = default);
    /// <summary>
    /// Generates AI marketing-content suggestions for an existing product.
    /// This is a non-saving operation — it does not persist anything; the caller
    /// applies the chosen fields separately via <see cref="UpdateAsync"/>.
    /// </summary>
    Task<ProductContentResponse> GenerateContentAsync(int id, CancellationToken cancellationToken = default);
}