using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shopit.Application.DTOs.Coupons;
using Shopit.Application.Interfaces;
using System.Security.Claims;

namespace Shopit.API.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/coupons")]
[Authorize(Roles = "Admin,Seller")]
public class CouponController : ControllerBase
{
    private readonly ICouponService _couponService;

    public CouponController(ICouponService couponService)
    {
        _couponService = couponService;
    }

    private int GetUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private bool IsAdmin() => User.IsInRole("Admin");

    [HttpPost]
    [ProducesResponseType(typeof(CouponResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateCouponRequest request)
    {
        var result = await _couponService.CreateAsync(GetUserId(), IsAdmin(), request);
        return CreatedAtAction(nameof(GetById), new { version = "1", id = result.Id }, result);
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CouponResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll()
    {
        var result = await _couponService.GetAllAsync(GetUserId(), IsAdmin());
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(CouponResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        var result = await _couponService.GetByIdAsync(id, GetUserId(), IsAdmin());
        return Ok(result);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(CouponResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCouponRequest request)
    {
        var result = await _couponService.UpdateAsync(id, GetUserId(), IsAdmin(), request);
        return Ok(result);
    }

    [HttpPut("{id:int}/deactivate")]
    [ProducesResponseType(typeof(CouponResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(int id)
    {
        var result = await _couponService.DeactivateAsync(id, GetUserId(), IsAdmin());
        return Ok(result);
    }
}
