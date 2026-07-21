# Managing Products

## What it does
Sellers create, edit, and remove the products in their store, and can list their own
products (including those in stores that aren't yet approved) for management screens.

## Who can use it
Signed-in users with the **Seller** role (managing their own store's products) or
**Admin**. A seller can only manage products that belong to a store they own; acting on
another seller's product is forbidden.

## How it works
- **List my products** returns a paginated list of the seller's own products. Unlike the
  public catalog, this includes products in Pending or Suspended stores, so a seller can
  manage inventory while their store awaits moderation.
- **Create a product** requires: `name`, `sku`, `price` (> 0), `categoryId`, `storeId`
  (a store you own), and `initialStock` (>= 0). An optional `description` and `imageUrl`
  can be supplied.
- **Update a product** replaces its `name`, `description`, `price`, `sku`, `categoryId`,
  and `stockQuantity`.
- **Delete a product** is a **soft delete** — the product is removed from listings but not
  physically erased.

## Endpoints
| Method | Route | Description |
|---|---|---|
| GET | `/api/v1/products/mine` | List my own products (paginated; same filters as the catalog). |
| POST | `/api/v1/products` | Create a product. Returns **201 Created**. |
| PUT | `/api/v1/products/{id}` | Update a product I own. |
| DELETE | `/api/v1/products/{id}` | Soft-delete a product I own. |

## Validation rules
- `name` and `sku` are required; `price` must be greater than 0.
- `categoryId` and `storeId` must be valid; `initialStock` / `stockQuantity` cannot be
  negative.
- A duplicate SKU is rejected with **409 Conflict**.

## Notes
- Stock set here is the same stock tracked by the Inventory feature — see
  [Managing Inventory and Stock](manage-inventory.md).
- Product images are managed through separate endpoints — see
  [Managing Product Images](product-images.md).
- Bulk product import from Excel/SFTP and AI content generation exist too, but those are
  **Admin-only** actions.
