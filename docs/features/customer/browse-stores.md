# Viewing a Seller's Storefront

## What it does
Every seller store has a public storefront page reachable by its **slug** (a URL-friendly
name). Shoppers can open a storefront to see the store's details and browse only the
products sold by that store.

## Who can use it
Anyone. Storefront pages are public — no login required.

## How it works
- A store is identified in the URL by its `slug` (for example `/stores/acme-gadgets`).
- Viewing the storefront returns the store's public details: name, slug, description, and
  status.
- Viewing the storefront's products returns a paginated product list filtered to that
  store, using the same product shape as the main catalog.
- Only **Approved** stores are visible. If a store is missing, Pending, Suspended, or
  Rejected, both endpoints return **404 Not Found** — and the products endpoint returns
  404 before listing anything.

## Endpoints
| Method | Route | Description |
|---|---|---|
| GET | `/api/v1/stores/{slug}` | Get a store's public details by slug. |
| GET | `/api/v1/stores/{slug}/products` | List that store's products (paginated: `pageNumber`, `pageSize`). |

## Notes
- The storefront is the buyer-facing view of a store. The seller-facing management side
  (creating a store, managing products, viewing orders) is covered in the Seller docs.
