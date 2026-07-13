# Viewing and Cancelling My Orders

## What it does
Lets a customer see their order history, open a single order for full details, and cancel
an order while it is still cancellable.

## Who can use it
Signed-in users with the **Customer** role. You only see and act on your own orders.
(Admins can also view any single order by ID and have a separate admin-wide order list.)

## How it works
- **Order history** is paginated. Each summary shows the order status, total, discount,
  shipping address, date, item count, payment status, and the per-store breakdown.
- **Order details** show the full order: all items and the per-store "store orders".
- An order's overall status is **rolled up** from its store orders:
  - If every store order is Cancelled, the order shows **Cancelled**.
  - Otherwise the order shows the *least-advanced* active store order, in this order:
    **Pending → Processing → Shipped → Delivered**.

## Cancelling an order
- A customer can cancel an order **only while every store order is still Pending**
  (i.e. no seller has started processing it, and it typically has not been paid).
- If any part has moved past Pending, cancellation is rejected with a message stating the
  current status.
- Cancelling restocks the ordered items.

## Order statuses
`Pending` → `Processing` → `Shipped` → `Delivered`, or `Cancelled`.

## Endpoints
| Method | Route | Description |
|---|---|---|
| GET | `/api/v1/orders` | List my orders (paginated: `page`, `pageSize`). |
| GET | `/api/v1/orders/{id}` | Get one of my orders in full. |
| PUT | `/api/v1/orders/{id}/cancel` | Cancel my order (only while fully Pending). |

## Notes
- Sellers advance the status of their own portion of an order — see the seller
  "Managing Store Orders" doc. As a buyer you see the combined, rolled-up status.
