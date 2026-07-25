using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Shopit.Application.AI;
using Shopit.Application.Common;
using Shopit.Application.Interfaces;
using Shopit.Application.Products;
using Shopit.Application.Products.DTOs;
using System.Security.Claims;
using DomainValidationException = Shopit.Domain.Exceptions.ValidationException;

namespace Shopit.API.Controllers;

/// <summary>
/// Manages product operations.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class ProductsController : ControllerBase
{
    private const long MaxImportFileSize = 10 * 1024 * 1024;
    private const long MaxImageFileSize = 5 * 1024 * 1024;
    private const string BlobContainerName = "product-images";

    private readonly IProductService _productService;
    private readonly IBlobStorageService _blobStorageService;
    private readonly ISftpProductImportService _sftpProductImportService;

    public ProductsController(
        IProductService productService,
        IBlobStorageService blobStorageService,
        ISftpProductImportService sftpProductImportService)
    {
        _productService = productService;
        _blobStorageService = blobStorageService;
        _sftpProductImportService = sftpProductImportService;
    }

    private int GetUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private bool IsAdmin() =>
        User.IsInRole("Admin");

    /// <summary>
    /// Gets all products with pagination, filtering, searching, and sorting.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResult<ProductResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PaginatedResult<ProductResponse>>> GetAll(
        [FromQuery] ProductQueryParameters queryParameters)
    {
        var products = await _productService.GetAllAsync(queryParameters);
        return Ok(products);
    }
    [HttpGet("search/semantic")]
    [AllowAnonymous]
    public async Task<IActionResult> SemanticSearch([FromQuery] string q, [FromQuery] int take = 10)
    {
        var results = await _productService.SemanticSearchAsync(q, take);
        return Ok(results);
    }

    [HttpPost("backfill-embeddings")]
    [Authorize(Roles = "Admin")]
    /// Backfills embeddings for all products that do not have an embedding yet. Admin only.
    /// This is a long-running operation and may take several minutes depending on the number of products
    public async Task<IActionResult> BackfillEmbeddings(CancellationToken ct)
    {
        var count = await _productService.BackfillEmbeddingsAsync(ct);
        return Ok(new { embedded = count });
    }
    /// <summary>
    /// Gets a product by ID.
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductResponse>> GetById(int id)
    {
        var product = await _productService.GetByIdAsync(id);
        return Ok(product);
    }

    /// <summary>
    /// Gets products owned by the current seller (or all products, for an admin),
    /// for use on seller/admin management screens. Unlike <see cref="GetAll"/>,
    /// this is not restricted to products in Approved stores, since a seller must
    /// be able to see and manage their own products while their store is
    /// Pending/Suspended awaiting moderation.
    /// </summary>
    [HttpGet("mine")]
    [Authorize(Roles = "Seller,Admin")]
    [ProducesResponseType(typeof(PaginatedResult<ProductResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PaginatedResult<ProductResponse>>> GetMine(
        [FromQuery] ProductQueryParameters queryParameters)
    {
        var products = await _productService.GetMineAsync(queryParameters, GetUserId(), IsAdmin());
        return Ok(products);
    }

    /// <summary>
    /// Creates a new product.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Seller,Admin")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductResponse>> Create([FromBody] CreateProductRequest request)
    {
        var product = await _productService.CreateAsync(request, GetUserId(), IsAdmin());

        return CreatedAtAction(
            nameof(GetById),
            new { id = product.Id, version = "1" },
            product);
    }

    /// <summary>
    /// Imports products from an Excel file.
    /// </summary>
    [HttpPost("import")]
    [Authorize(Roles = "Admin")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxImportFileSize)]
    [ProducesResponseType(typeof(ImportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ImportResultDto>> Import(
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            throw new DomainValidationException("Excel file is required.");

        var extension = Path.GetExtension(file.FileName);

        if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new DomainValidationException("Only .xlsx Excel files are allowed.");

        if (file.Length > MaxImportFileSize)
            throw new DomainValidationException("File size must not exceed 10MB.");

        using var stream = file.OpenReadStream();
        var result = await _productService.ImportAsync(stream, cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Imports products from the Excel file hosted on the configured SFTP server.
    /// Downloads the file over SFTP and runs it through the same import pipeline as
    /// <see cref="Import"/>. Returns 502 if the SFTP server is unreachable and 404 if
    /// the configured file does not exist.
    /// </summary>
    [HttpPost("import-from-sftp")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ImportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ImportResultDto>> ImportFromSftp(CancellationToken cancellationToken)
    {
        var result = await _sftpProductImportService.ImportProductsFromSftpAsync(cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Updates an existing product.
    /// </summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Seller,Admin")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductResponse>> Update(
        int id,
        [FromBody] UpdateProductRequest request)
    {
        var product = await _productService.UpdateAsync(id, request, GetUserId(), IsAdmin());
        return Ok(product);
    }

    /// <summary>
    /// Soft deletes a product.
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Seller,Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        await _productService.DeleteAsync(id, GetUserId(), IsAdmin());
        return NoContent();
    }

    /// <summary>
    /// Uploads an image for a product. Admin only.
    /// </summary>
    [HttpPost("{id:int}/image")]
    [Authorize(Roles = "Seller,Admin")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxImageFileSize)]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadImage(int id, IFormFile? file)
    {
        if (file is null || file.Length == 0)
            throw new DomainValidationException("Image file is required.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension != ".jpg" && extension != ".jpeg" && extension != ".png")
            throw new DomainValidationException("Only jpg and png images are allowed.");

        if (file.Length > MaxImageFileSize)
            throw new DomainValidationException("Image must not exceed 5MB.");

        var url = await _productService.UploadImageAsync(id, file, _blobStorageService, BlobContainerName, GetUserId(), IsAdmin());

        return Ok(new { imageUrl = url });
    }

    /// <summary>
    /// Generates AI marketing-content suggestions for a product (description, 5 features,
    /// SEO title, meta description). Admin only. Returns a suggestion for review and does
    /// NOT persist anything — apply chosen fields via PUT /products/{id}. Rate limited.
    /// </summary>
    [HttpPost("{id:int}/generate-content")]
    [Authorize(Roles = "Admin")]
    [EnableRateLimiting("GeminiContentGeneration")]
    [ProducesResponseType(typeof(ProductContentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ProductContentResponse>> GenerateContent(
        int id,
        CancellationToken cancellationToken)
    {
        var content = await _productService.GenerateContentAsync(id, cancellationToken);
        return Ok(content);
    }

    /// <summary>
    /// Deletes the image of a product. Admin only.
    /// </summary>
    [HttpDelete("{id:int}/image")]
    [Authorize(Roles = "Seller,Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteImage(int id)
    {
        await _productService.DeleteImageAsync(id, _blobStorageService, BlobContainerName, GetUserId(), IsAdmin());
        return NoContent();
    }
}