using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shopit.Application.DTOs.Payments;
using Shopit.Application.Interfaces;
using System.Security.Claims;

namespace Shopit.API.Controllers;

[ApiController]
[Route("api/v1/payments")]
[Authorize]
public class PaymentController : ControllerBase
{
    private readonly IPaymentService _paymentService;

    public PaymentController(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpPost]
    public async Task<IActionResult> ProcessPayment([FromBody] ProcessPaymentRequest request)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _paymentService.ProcessPaymentAsync(request, userId);
        return Ok(result);
    }

    [HttpGet("order/{orderId}")]
    public async Task<IActionResult> GetByOrderId(int orderId)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _paymentService.GetByOrderIdAsync(orderId, userId);
        return Ok(result);
    }

    [HttpPost("{paymentId}/refund")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Refund(int paymentId)
    {
        var result = await _paymentService.RefundAsync(paymentId);
        return Ok(result);
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAll([FromQuery] PaymentQueryParameters parameters)
    {
        var result = await _paymentService.GetAllPaymentsAsync(parameters);
        return Ok(result);
    }
}