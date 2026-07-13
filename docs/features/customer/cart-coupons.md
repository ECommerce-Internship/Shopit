# Applying a Coupon to the Cart

## What it does
Customers can apply a discount coupon to their cart by entering a coupon code. The
discount is reflected in the cart's totals, and can be removed again before checkout.

## Who can use it
Signed-in users with the **Customer** role, operating on their own cart.

## How it works
- Apply a coupon by sending its `code`. The code is required (an empty code is rejected).
- A coupon can be either a **percentage** discount or a **fixed-amount** discount.
- After a coupon is applied, the cart response shows `couponCode`, the discount
  (`discountPercentage` and/or `discountAmount`), and the reduced `finalTotal`.
- Only one coupon applies to the cart at a time. Removing the coupon returns the cart to
  its undiscounted total.
- A coupon can have a usage limit. If the coupon has reached that limit, the discount is
  rejected **at checkout** (order placement) rather than silently ignored.

## Endpoints
| Method | Route | Description |
|---|---|---|
| POST | `/api/v1/cart/coupon` | Apply a coupon by `code`. |
| DELETE | `/api/v1/cart/coupon` | Remove the applied coupon. |

## Notes
- Applying or removing a coupon returns the full updated cart, so the client can show the
  new totals immediately.
- The discount carries through to the order: the placed order records a `discountAmount`.
