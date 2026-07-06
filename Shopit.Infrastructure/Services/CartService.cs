using Microsoft.EntityFrameworkCore;
using Shopit.Application.DTOs;
using Shopit.Application.Interfaces;
using Shopit.Domain.Entities;
using Shopit.Domain.Enums;
using Shopit.Domain.Exceptions;
using Shopit.Infrastructure.Data;

namespace Shopit.Infrastructure.Services;

public class CartService : ICartService
{
    private readonly AppDbContext _context;

    public CartService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<CartResponse> GetCartAsync(int userId)
    {
        var cart = await GetOrCreateCartAsync(userId);
        return MapToResponse(cart);
    }

    public async Task<CartResponse> AddItemAsync(int userId, AddCartItemRequest request)
    {
        var cart = await GetOrCreateCartAsync(userId);

        var product = await _context.Products
            .Include(p => p.Inventory)
            .FirstOrDefaultAsync(p => p.Id == request.ProductId && !p.IsDeleted);

        if (product == null)
            throw new NotFoundException($"Product with ID {request.ProductId} was not found.");

        if (product.Inventory == null || product.Inventory.Quantity < request.Quantity)
            throw new ValidationException($"Insufficient stock for '{product.Name}'.");

        var existingItem = cart.CartItems.FirstOrDefault(ci => ci.ProductId == request.ProductId);

        if (existingItem != null)
        {
            var newQuantity = existingItem.Quantity + request.Quantity;
            if (product.Inventory.Quantity < newQuantity)
                throw new ValidationException($"Insufficient stock for '{product.Name}'.");
            existingItem.Quantity = newQuantity;
        }
        else
        {
            cart.CartItems.Add(new CartItem
            {
                CartId = cart.Id,
                ProductId = request.ProductId,
                Quantity = request.Quantity
            });
        }

        await _context.SaveChangesAsync();
        await _context.Entry(cart).ReloadAsync();
        cart = await GetCartWithDetailsAsync(cart.Id);
        return MapToResponse(cart);
    }

    public async Task<CartResponse> UpdateItemAsync(int userId, int cartItemId, UpdateCartItemRequest request)
    {
        var cart = await GetOrCreateCartAsync(userId);

        var cartItem = cart.CartItems.FirstOrDefault(ci => ci.Id == cartItemId);
        if (cartItem == null)
            throw new NotFoundException($"Cart item with ID {cartItemId} was not found.");

        var product = await _context.Products
            .Include(p => p.Inventory)
            .FirstOrDefaultAsync(p => p.Id == cartItem.ProductId && !p.IsDeleted);

        if (product == null)
            throw new NotFoundException($"Product was not found.");

        if (product.Inventory == null || product.Inventory.Quantity < request.Quantity)
            throw new ValidationException($"Insufficient stock for '{product.Name}'.");

        cartItem.Quantity = request.Quantity;
        await _context.SaveChangesAsync();

        cart = await GetCartWithDetailsAsync(cart.Id);
        return MapToResponse(cart);
    }

    public async Task RemoveItemAsync(int userId, int cartItemId)
    {
        var cart = await GetOrCreateCartAsync(userId);

        var cartItem = cart.CartItems.FirstOrDefault(ci => ci.Id == cartItemId);
        if (cartItem == null)
            throw new NotFoundException($"Cart item with ID {cartItemId} was not found.");

        _context.CartItems.Remove(cartItem);
        await _context.SaveChangesAsync();
    }

    public async Task ClearCartAsync(int userId)
    {
        var cart = await GetOrCreateCartAsync(userId);

        _context.CartItems.RemoveRange(cart.CartItems);
        cart.CouponId = null;
        await _context.SaveChangesAsync();
    }

    public async Task<CartResponse> ApplyCouponAsync(int userId, ApplyCouponRequest request)
    {
        var cart = await GetOrCreateCartAsync(userId);

        var coupon = await _context.Coupons
            .FirstOrDefaultAsync(c => c.Code.ToLower() == request.Code.ToLower());

        if (coupon == null)
            throw new ValidationException("Coupon code is invalid.");

        if (!coupon.IsActive)
            throw new ValidationException("Coupon is no longer active.");

        if (coupon.ExpiresAt.HasValue && coupon.ExpiresAt < DateTime.UtcNow)
            throw new ValidationException("Coupon has expired.");

        cart.CouponId = coupon.Id;
        await _context.SaveChangesAsync();

        cart = await GetCartWithDetailsAsync(cart.Id);
        return MapToResponse(cart);
    }

    public async Task<CartResponse> RemoveCouponAsync(int userId)
    {
        var cart = await GetOrCreateCartAsync(userId);
        cart.CouponId = null;
        await _context.SaveChangesAsync();

        cart = await GetCartWithDetailsAsync(cart.Id);
        return MapToResponse(cart);
    }

    private async Task<Cart> GetOrCreateCartAsync(int userId)
    {
        var cart = await _context.Carts
            .Include(c => c.CartItems)
                .ThenInclude(ci => ci.Product)
                .ThenInclude(p => p.Store)
            .Include(c => c.Coupon)
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Status == CartStatus.Active);

        if (cart == null)
        {
            cart = new Cart { UserId = userId, Status = CartStatus.Active };
            _context.Carts.Add(cart);
            await _context.SaveChangesAsync();
        }

        return cart;
    }

    private async Task<Cart> GetCartWithDetailsAsync(int cartId)
    {
        return await _context.Carts
            .Include(c => c.CartItems)
                .ThenInclude(ci => ci.Product)
                .ThenInclude(p => p.Store)
            .Include(c => c.Coupon)
            .FirstAsync(c => c.Id == cartId);
    }

    private static CartResponse MapToResponse(Cart cart)
    {
        var items = cart.CartItems.Select(ci => new CartItemResponse
        {
            Id = ci.Id,
            ProductId = ci.ProductId,
            ProductName = ci.Product.Name,
            SKU = ci.Product.SKU,
            UnitPrice = ci.Product.Price,
            Quantity = ci.Quantity,
            Subtotal = ci.Product.Price * ci.Quantity,
            StoreId = ci.Product.Store.Id,
            StoreName = ci.Product.Store.Name,
            StoreSlug = ci.Product.Store.Slug
        }).ToList();

        var subtotal = items.Sum(i => i.Subtotal);
        decimal discountAmount = 0;
        decimal? discountPercentage = null;

        if (cart.Coupon != null)
        {
            if (cart.Coupon.DiscountType == CouponDiscountType.Percent)
            {
                discountPercentage = cart.Coupon.DiscountValue;
                discountAmount = subtotal * (cart.Coupon.DiscountValue / 100);
            }
            else
            {
                discountAmount = cart.Coupon.DiscountValue;
            }
        }

        var finalTotal = subtotal - discountAmount;

        return new CartResponse
        {
            Id = cart.Id,
            Items = items,
            Subtotal = subtotal,
            CouponCode = cart.Coupon?.Code,
            DiscountPercentage = discountPercentage,
            DiscountAmount = cart.Coupon != null ? discountAmount : null,
            FinalTotal = finalTotal < 0 ? 0 : finalTotal
        };
    }
}