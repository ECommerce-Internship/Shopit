# Managing Inventory and Stock

## What it does
Lets a seller see and adjust the stock level for their products and set a **low-stock
threshold** so they get warned before running out.

## Who can use it
Signed-in users with the **Seller** role (their own products) or **Admin**. A seller can
only view and change inventory for products in stores they own.

## How it works
- View the inventory record for a product: its current `quantity`, its
  `lowStockThreshold`, whether it is currently low (`isLowStock`), the store it belongs to,
  and when it was last updated.
- **Update stock** sets the product's available quantity. Quantity cannot be negative.
- **Update threshold** sets the level at or below which the product counts as "low stock".
- Stock is automatically **decremented** when orders are placed and **restored** when an
  order (or a store order) is cancelled.
- When stock crosses below the threshold, Shopit can raise a **low-stock alert** (email
  notification) so the item can be restocked.

## Endpoints
| Method | Route | Description |
|---|---|---|
| GET | `/api/v1/inventory/{productId}` | Get the inventory record for one of my products. |
| PUT | `/api/v1/inventory/{productId}/stock` | Set the product's stock `quantity`. |
| PUT | `/api/v1/inventory/{productId}/threshold` | Set the product's low-stock `threshold`. |

## Notes
- The platform-wide inventory list and the cross-store low-stock list are **Admin-only**
  views; a seller manages inventory one product at a time.
- Stock shown here is the same value surfaced as `stockQuantity` on the product in the
  catalog and used to block checkout when insufficient.
