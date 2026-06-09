using Asp.Versioning;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Shopit.Application.DTOs.Auth;
using Shopit.Application.Interfaces;
using Shopit.Domain.Entities;
using Shopit.Domain.Enums;

namespace Shopit.API.Controllers;

/// <summary>
/// Handles user authentication.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IValidator<RegisterRequest> _registerValidator;

    public AuthController(
        IAuthService authService,
        IValidator<RegisterRequest> registerValidator)
    {
        _authService = authService;
        _registerValidator = registerValidator;
    }

    /// <summary>
    /// Registers a new customer account.
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var validation = await _registerValidator.ValidateAsync(request);
        if (!validation.IsValid)
            return BadRequest(validation.Errors.Select(e => e.ErrorMessage));

        var response = await _authService.RegisterAsync(request);
        return CreatedAtAction(nameof(Register), response);
    }

    /// <summary>
    /// Authenticates a user and returns a token pair.
    /// </summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var response = await _authService.LoginAsync(request);
        return Ok(response);
    }
}