# Managing Store Orders (Fulfillment)

## What it does
When a customer's order includes products from a seller's store, Shopit creates a
**store order** — the seller's portion of that buyer's order. Sellers view their store
orders and advance each one through the fulfillment stages.

## Who can use it
Signed-in users with the **Seller** role (their own store orders) or **Admin**. A seller
can only view and manage store orders for stores they own.

## How it works
- A buyer's order is split into one store order per store at checkout. Each store order
  carries its own status, its subtotal, the platform **commission**, and the seller's
  **net amount** (subtotal minus commission).
- A seller sees the list of their store orders across all their stores, and can open one
  for full details (items, shipping address, amounts).
- The seller advances a store order's status. Allowed transitions are strictly enforced:

  | From | Allowed next |
  |---|---|
  | `Pending` | `Processing`, `Cancelled` |
  | `Processing` | `Shipped` |
  | `Shipped` | `Delivered` |
  | `Delivered` | (none — final) |
  | `Cancelled` | (none — final) |

- Any other transition is rejected. Cancelling a store order **restocks only that store's
  items**.
- Store orders typically move to `Processing` automatically once the buyer **pays** for
  the order.

## Endpoints
| Method | Route | Description |
|---|---|---|
| GET | `/api/v1/store-orders/mine` | List my store orders across my stores. |
| GET | `/api/v1/store-orders/{id}` | Get one of my store orders in full. |
| PUT | `/api/v1/store-orders/{id}/status` | Advance a store order's status (`status`). |

## What a store order looks like
`storeOrderId`, `orderId`, `storeId` + `storeName`, `status`, `subTotal`,
`commissionAmount`, `sellerNetAmount`, `shippingAddress`, `createdAt`, and the list of
`items`.

## Notes
- The buyer sees a single rolled-up status for their whole order, derived from all its
  store orders (see the customer "Viewing and Cancelling My Orders" doc). Each seller only
  controls their own portion.
