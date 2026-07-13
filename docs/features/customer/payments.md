# Paying for an Order

## What it does
Lets a customer pay for an order they placed and check the payment status for an order.
Paying moves the order forward so sellers can begin fulfillment.

## Who can use it
Any signed-in user can pay for and view the payment of **their own** order. (Issuing
refunds and viewing all payments across the platform are administrator actions.)

## How it works
- The customer submits a payment for an order, choosing a payment method.
- Payment is **simulated** in Shopit (there is no real payment gateway): a successful
  payment is marked **Paid** and given a generated transaction reference and paid-at time.
- When a payment succeeds, every store order in that order advances from **Pending** to
  **Processing**, signalling sellers to start fulfilling.
- An order can only be paid **once** — attempting to pay an already-paid order is
  rejected (409 Conflict).
- A request can set `simulateFailure: true` to force a **Failed** payment, useful for
  testing. Failed payments do not advance the order.

## Payment methods
`Card`, `CashOnDelivery`, `PayPal`, `BankTransfer`.

## Payment statuses
`Pending`, `Paid`, `Failed`, `Refunded`.

## Endpoints
| Method | Route | Description |
|---|---|---|
| POST | `/api/v1/payments` | Pay for an order (`orderId`, `paymentMethod`, optional `simulateFailure`). |
| GET | `/api/v1/payments/order/{orderId}` | Get the payment for one of your orders. |

## Notes
- You can only pay for and view payments on orders you own; paying for someone else's
  order is forbidden.
- Refunds are admin-only. When an admin refunds a payment, the related store orders are
  cancelled.
