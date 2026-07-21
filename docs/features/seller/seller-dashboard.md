# Seller Dashboard (Analytics)

## What it does
Gives a seller an analytics overview of their own store(s): earnings, order counts, best
sellers, revenue over time, and a breakdown of orders by status. All figures are scoped to
the signed-in seller's stores only.

## Who can use it
Signed-in users with the **Seller** role. The data is always limited to the stores you own.

## How it works
The dashboard is made of four read-only views:

- **Summary** — headline numbers: gross sales, platform commission, net earnings, total
  orders, low-stock count, and today's orders.
- **Top products** — the seller's top 10 products by units sold.
- **Revenue** — gross revenue grouped by a period: `day` (default), `week`, or `month`.
- **Orders by status** — how many store orders sit in each status
  (Pending / Processing / Shipped / Delivered / Cancelled).

## Endpoints
| Method | Route | Description |
|---|---|---|
| GET | `/api/v1/seller/dashboard/summary` | Sales, commission, net earnings, order and stock counts. |
| GET | `/api/v1/seller/dashboard/top-products` | Top 10 products by units sold. |
| GET | `/api/v1/seller/dashboard/revenue?period=day` | Gross revenue by `day`, `week`, or `month`. |
| GET | `/api/v1/seller/dashboard/orders-by-status` | Store-order counts grouped by status. |

## Notes
- "Net earnings" is gross sales minus the platform commission, matching the
  `sellerNetAmount` recorded on each store order.
- Admins have a separate, platform-wide dashboard; this one is per-seller.
