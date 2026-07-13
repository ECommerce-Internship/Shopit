# Creating and Managing a Store

## What it does
A seller opens a store on Shopit to sell products. The store gets a public storefront
once an administrator approves it. Sellers can create a store and list the stores they own.

## Who can use it
Signed-in users with the **Seller** role. (You get the Seller role by registering as a
seller — see the Auth docs.)

## How it works
- A seller creates a store by giving it a **name** (required, up to 100 characters) and an
  optional **description** (up to 1000 characters). A URL-friendly **slug** is generated
  for the storefront.
- A newly created store starts in status **Pending** and is **not** publicly visible until
  an administrator approves it. Approval, rejection, and suspension are admin actions.
- Each store has a **commission rate** — the platform's cut on the store's sales, applied
  per order at checkout.
- A seller can list all stores they own, seeing each store's status and commission rate.

## Store statuses
| Status | Meaning |
|---|---|
| `Pending` | Newly created, awaiting admin review. Not publicly visible. |
| `Approved` | Live. Storefront and products are publicly visible and orderable. |
| `Suspended` | Temporarily hidden by an admin. Not orderable. |
| `Rejected` | Declined by an admin. Not visible. |

## Endpoints
| Method | Route | Description |
|---|---|---|
| POST | `/api/v1/stores` | Create a store (`name`, optional `description`). Returns **201 Created**. |
| GET | `/api/v1/stores` | List the stores I own. |

## What a store looks like
`id`, `name`, `slug`, `description`, `status`, `commissionRate`, `ownerUserId`,
`createdAt`.

## Notes
- While a store is Pending or Suspended, the seller can still create and manage its
  products (see [Managing Products](manage-products.md)) even though buyers can't see them
  yet. Products only appear in the public catalog once the store is Approved.
- The public buyer-facing view of a store is documented in the customer
  "Viewing a Seller's Storefront" doc.
