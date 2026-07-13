# Shopping Cart

## What it does
The shopping cart holds the products a customer intends to buy before checking out. A
customer can view the cart, add products, change quantities, remove items, or empty the
cart entirely. The cart can contain products from several different stores at once.

## Who can use it
Signed-in users with the **Customer** role only. Every cart belongs to the logged-in
customer; you always operate on your own cart. (Sellers and admins do not have a cart.)

## How it works
- Each customer has one active cart. Adding, updating, or removing items returns the
  **full updated cart** so the client always has the latest totals.
- Quantities must be at least 1. Adding a product requires a valid product ID and a
  quantity of 1 or more.
- The cart response shows, per item: product name, SKU, unit price, quantity, line
  subtotal, and which store the item comes from.
- The cart also shows the overall `subtotal`, any applied coupon and its discount, and
  the `finalTotal`. Coupons are covered in [Cart Coupons](cart-coupons.md).

## Endpoints
| Method | Route | Description |
|---|---|---|
| GET | `/api/v1/cart` | Get the current customer's cart. |
| POST | `/api/v1/cart/items` | Add an item (`productId`, `quantity`). |
| PUT | `/api/v1/cart/items/{cartItemId}` | Change an item's quantity. |
| DELETE | `/api/v1/cart/items/{cartItemId}` | Remove a single item. |
| DELETE | `/api/v1/cart` | Clear the whole cart. |

## Cart contents
The cart returns: `id`, a list of `items` (each with `productId`, `productName`, `sku`,
`unitPrice`, `quantity`, `subtotal`, and store info), `subtotal`, optional `couponCode` /
`discountPercentage` / `discountAmount`, and `finalTotal`.

## Notes
- Stock and store-availability are re-checked at checkout, not while editing the cart —
  so an item can sit in your cart but still fail at order time if it sold out or its store
  became unavailable. See [Placing an Order (Checkout)](place-order-checkout.md).
