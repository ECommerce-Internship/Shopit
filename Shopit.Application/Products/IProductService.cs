using Microsoft.AspNetCore.Http;
using Shopit.Application.AI;
using Shopit.Application.Common;
using Shopit.Application.Interfaces;
using Shopit.Application.Products.DTOs;

namespace Shopit.Application.Products;

public interface IProductService
{
    Task<PaginatedResult<ProductResponse>> GetAllAsync(ProductQueryParameters queryParameters);

    /// <summary>
    /// Gets products owned by the current caller for seller/admin management screens.
    /// Unlike <see cref="GetAllAsync"/>, this is NOT restricted to products in
    /// Approved stores — a seller must be able to see and manage their own products
    /// while their store is Pending/Suspended, even though those products are not
    /// publicly listable. A seller only sees products from stores they own; an
    /// admin sees all products (optionally filtered to one store via StoreId).
    /// </summary>
    Task<PaginatedResult<ProductResponse>> GetMineAsync(ProductQueryParameters queryParameters, int userId, bool isAdmin);

    Task<ProductResponse> GetByIdAsync(int id);

    Task<ProductResponse> CreateAsync(CreateProductRequest request, int userId, bool isAdmin);

    Task<ProductResponse> UpdateAsync(int id, UpdateProductRequest request, int userId, bool isAdmin);

    Task DeleteAsync(int id, int userId, bool isAdmin);

    Task<ImportResultDto> ImportAsync(Stream fileStream, CancellationToken cancellationToken = default);

    Task<string> UploadImageAsync(int productId, IFormFile file, IBlobStorageService blobStorageService, string containerName, int userId, bool isAdmin);

    Task DeleteImageAsync(int productId, IBlobStorageService blobStorageService, string containerName, int userId, bool isAdmin);

    /// <summary>
    /// Generates AI marketing-content suggestions for an existing product.
    /// This is a non-saving operation — it does not persist anything; the caller
    /// applies the chosen fields separately via <see cref="UpdateAsync"/>.
    /// </summary>
    Task<ProductContentResponse> GenerateContentAsync(int id, CancellationToken cancellationToken = default);
}