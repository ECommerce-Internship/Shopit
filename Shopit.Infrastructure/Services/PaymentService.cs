using Microsoft.EntityFrameworkCore;
using Serilog;
using Shopit.Application.DTOs.Payments;
using Shopit.Application.Interfaces;
using Shopit.Domain.Entities;
using Shopit.Domain.Enums;
using Shopit.Domain.Exceptions;
using Shopit.Infrastructure.Data;

namespace Shopit.Infrastructure.Services;

public class PaymentService : IPaymentService
{
    private readonly AppDbContext _context;
    private readonly IEmailService _emailService;

    public PaymentService(AppDbContext context, IEmailService emailService)
    {
        _context = context;
        _emailService = emailService;
    }

    public async Task<PaymentResponse> ProcessPaymentAsync(ProcessPaymentRequest request, int currentUserId)
    {
        var order = await _context.Orders
            .Include(o => o.User)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId);

        if (order is null)
            throw new NotFoundException($"Order with ID {request.OrderId} was not found.");

        if (order.UserId != currentUserId)
            throw new ForbiddenException("You are not authorized to pay for this order.");

        var existingPayment = await _context.Payments
            .FirstOrDefaultAsync(p => p.OrderId == request.OrderId
                && p.Status == PaymentStatus.Paid);

        if (existingPayment is not null)
            throw new ConflictException($"Order {request.OrderId} has already been paid.");

       var status = request.SimulateFailure
        ? PaymentStatus.Failed
        : PaymentStatus.Paid;

        var payment = new Payment
        {
            OrderId = request.OrderId,
            Amount = order.TotalAmount,
            Method = request.PaymentMethod,
            Status = status,
            TransactionRef = Guid.NewGuid().ToString("N"),
            PaidAt = status == PaymentStatus.Paid ? DateTime.UtcNow : null
        };

        _context.Payments.Add(payment);

        if (status == PaymentStatus.Paid)
        {
            order.Status = OrderStatus.Processing;
            await _emailService.SendOrderConfirmationAsync(order.Id, order.User.Email);
            Log.Information("Payment successful for Order {OrderId}", order.Id);
        }
        else
        {
            Log.Warning("Payment failed for Order {OrderId}", order.Id);
        }

        await _context.SaveChangesAsync();

        return MapToResponse(payment);
    }

    public async Task<PaymentResponse> GetByOrderIdAsync(int orderId, int currentUserId)
    {
        var order = await _context.Orders
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order is null)
            throw new NotFoundException($"Order with ID {orderId} was not found.");

        if (order.UserId != currentUserId)
            throw new ForbiddenException("You are not authorized to view this payment.");

        var payment = await _context.Payments
        .OrderByDescending(p => p.Id)
        .FirstOrDefaultAsync(p => p.OrderId == orderId);

        if (payment is null)
            throw new NotFoundException($"No payment found for Order {orderId}.");

        return MapToResponse(payment);
    }

    public async Task<PaymentResponse> RefundAsync(int paymentId)
    {
        var payment = await _context.Payments
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.Id == paymentId);

        if (payment is null)
            throw new NotFoundException($"Payment with ID {paymentId} was not found.");

        if (payment.Status != PaymentStatus.Paid)
            throw new ConflictException($"Payment {paymentId} cannot be refunded because its status is {payment.Status}.");

        payment.Status = PaymentStatus.Refunded;
        payment.Order.Status = OrderStatus.Cancelled;

        await _context.SaveChangesAsync();

        Log.Information("Payment {PaymentId} refunded for Order {OrderId}", paymentId, payment.OrderId);

        return MapToResponse(payment);
    }

    public async Task<List<PaymentResponse>> GetAllPaymentsAsync(PaymentQueryParameters parameters)
    {
        var pageNumber = parameters.PageNumber <= 0 ? 1 : parameters.PageNumber;
        var pageSize = parameters.PageSize <= 0 ? 10 : parameters.PageSize;
        if (pageSize > 100) pageSize = 100;

        var query = _context.Payments.AsQueryable();

        if (parameters.Status.HasValue)
            query = query.Where(p => p.Status == parameters.Status.Value);

        var payments = await query
            .OrderByDescending(p => p.PaidAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        Log.Information("Admin retrieved {Count} payments", payments.Count);

        return payments.Select(MapToResponse).ToList();
    }

    private static PaymentResponse MapToResponse(Payment payment) => new()
    {
        Id = payment.Id,
        OrderId = payment.OrderId,
        Amount = payment.Amount,
        Status = payment.Status,
        Method = payment.Method,
        TransactionRef = payment.TransactionRef,
        PaidAt = payment.PaidAt
    };
}