# Placing an Order (Checkout)

## What it does
Turns the customer's active cart into an order. The customer provides a shipping address,
and Shopit creates the order, reserves stock, and splits the order across the stores the
items came from.

## Who can use it
Signed-in users with the **Customer** role. You check out your own cart.

## How it works
1. The customer sends a shipping address to place the order.
2. Shopit validates the cart before creating the order:
   - The cart must not be empty.
   - Every item's store must still be **Approved** — otherwise checkout fails listing the
     unavailable products.
   - Every item must have enough stock — otherwise checkout fails listing the
     out-of-stock products.
   - If a coupon is applied, it must not have exceeded its usage limit.
3. The order is **split per store**: one "store order" is created for each distinct store
   in the cart. For each store order, Shopit calculates the store's subtotal, the
   platform **commission**, and the seller's **net amount**.
4. Each store order starts in status **Pending**. Stock is decremented for the ordered
   items.

## Endpoint
| Method | Route | Description |
|---|---|---|
| POST | `/api/v1/orders` | Place an order from the current cart (`shippingAddress`). Returns **201 Created** with the new order. |

## Request
```json
{ "shippingAddress": "123 Main St, City, Country" }
```

## What you get back
The created order includes: `id`, overall `status`, `totalAmount`, `discountAmount`,
`shippingAddress`, `createdAt`, the list of `items`, and the per-store breakdown in
`storeOrders` (each with its store, status, subtotal, and items).

## Notes
- Placing an order does **not** pay for it. Payment is a separate step — see
  [Paying for an Order](payments.md).
- Because an order can span multiple stores, each store fulfills (and can cancel) its own
  portion independently. See [My Orders](my-orders.md) for how the overall status rolls up.
