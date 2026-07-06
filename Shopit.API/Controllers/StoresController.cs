using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shopit.Application.Common;
using Shopit.Application.DTOs.Stores;
using Shopit.Application.Interfaces;
using Shopit.Application.Products;
using Shopit.Application.Products.DTOs;
using System.Security.Claims;

namespace Shopit.API.Controllers;

[ApiController]
[ApiVersion("1.0")]
public class StoresController : ControllerBase
{
    private readonly IStoreService _storeService;
    private readonly IProductService _productService;

    public StoresController(IStoreService storeService, IProductService productService)
    {
        _storeService = storeService;
        _productService = productService;
    }

    private int GetUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ----- Seller endpoints -----

    [HttpPost]
    [Route("api/v{version:apiVersion}/stores")]
    [Authorize(Roles = "Seller")]
    [ProducesResponseType(typeof(StoreResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateStore([FromBody] CreateStoreRequest request)
    {
        var result = await _storeService.CreateStoreAsync(GetUserId(), request);
        return CreatedAtAction(nameof(GetMyStores), new { version = "1" }, result);
    }

    [HttpGet]
    [Route("api/v{version:apiVersion}/stores")]
    [Authorize(Roles = "Seller")]
    [ProducesResponseType(typeof(IReadOnlyList<StoreResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMyStores()
    {
        var result = await _storeService.GetMyStoresAsync(GetUserId());
        return Ok(result);
    }

    // ----- Public storefront endpoints -----

    [HttpGet]
    [Route("api/v{version:apiVersion}/stores/{slug}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(StoreResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStorefront(string slug)
    {
        var result = await _storeService.GetStoreBySlugAsync(slug);
        return Ok(result);
    }

    [HttpGet]
    [Route("api/v{version:apiVersion}/stores/{slug}/products")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PaginatedResult<ProductResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStorefrontProducts(string slug, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
    {
        // Hidden/missing stores 404 before any products are listed.
        await _storeService.GetStoreBySlugAsync(slug);

        var products = await _productService.GetAllAsync(new ProductQueryParameters
        {
            StoreSlug = slug,
            PageNumber = pageNumber,
            PageSize = pageSize
        });

        return Ok(products);
    }

    // ----- Admin moderation endpoints -----

    [HttpGet]
    [Route("api/v{version:apiVersion}/admin/stores/pending")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(IReadOnlyList<StoreResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPendingStores()
    {
        var result = await _storeService.GetPendingStoresAsync();
        return Ok(result);
    }

    [HttpPut]
    [Route("api/v{version:apiVersion}/admin/stores/{id:int}/approve")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(StoreResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApproveStore(int id)
    {
        var result = await _storeService.ApproveStoreAsync(id);
        return Ok(result);
    }

    [HttpPut]
    [Route("api/v{version:apiVersion}/admin/stores/{id:int}/reject")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(StoreResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RejectStore(int id)
    {
        var result = await _storeService.RejectStoreAsync(id);
        return Ok(result);
    }

    [HttpPut]
    [Route("api/v{version:apiVersion}/admin/stores/{id:int}/suspend")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(StoreResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SuspendStore(int id)
    {
        var result = await _storeService.SuspendStoreAsync(id);
        return Ok(result);
    }
}
