using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shopit.Application.AI;
using Shopit.Application.Common;
using Shopit.Application.Products;
using Shopit.Application.Products.DTOs;
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
    private readonly IProductService _productService;

    public ProductsController(IProductService productService)
    {
        _productService = productService;
    }

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
    /// Creates a new product.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductResponse>> Create([FromBody] CreateProductRequest request)
    {
        var product = await _productService.CreateAsync(request);

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
    {
        throw new DomainValidationException("Excel file is required.");
    }

    var extension = Path.GetExtension(file.FileName);

    if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
    {
        throw new DomainValidationException("Only .xlsx Excel files are allowed.");
    }

    if (file.Length > MaxImportFileSize)
    {
        throw new DomainValidationException("File size must not exceed 10MB.");
    }

    using var stream = file.OpenReadStream();

    var result = await _productService.ImportAsync(stream, cancellationToken);

    return Ok(result);
}

    /// <summary>
    /// Updates an existing product.
    /// </summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductResponse>> Update(
        int id,
        [FromBody] UpdateProductRequest request)
    {
        var product = await _productService.UpdateAsync(id, request);

        return Ok(product);
    }

    /// <summary>
    /// Soft deletes a product.
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        await _productService.DeleteAsync(id);

        return NoContent();
    }

    /// <summary>
    /// Generates and persists AI content (description, SEO title, meta description, features) for a product.
    /// </summary>
    [HttpPost("{id:int}/generate-content")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductResponse>> GenerateContent(
        int id,
        [FromBody] ApplyProductContentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _productService.ApplyGeneratedContentAsync(id, request.Specs, cancellationToken);

        return Ok(result);
    }
}