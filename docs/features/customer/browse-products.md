# Browsing and Searching Products

## What it does
Lets shoppers browse the Shopit catalog, search for products, filter and sort results,
and open a single product to see its full details (price, description, image, stock,
average rating, and review count).

## Who can use it
Anyone. These endpoints are public — no login is required to browse or view products.
Only products that belong to an **Approved** store appear in the public catalog.

## How it works
- The product list is **paginated**. You ask for a page and a page size and get back the
  items for that page plus paging information.
- You can narrow the list with query parameters and change the ordering.
- Opening a product by its ID returns the full product, including its store, category,
  current stock quantity, average rating, and number of reviews.

## Endpoints
| Method | Route | Description |
|---|---|---|
| GET | `/api/v1/products` | List products (paginated, filterable, sortable). |
| GET | `/api/v1/products/{id}` | Get one product by its ID. Returns 404 if it doesn't exist. |

## Query parameters (for `GET /products`)
| Parameter | Meaning |
|---|---|
| `search` | Free-text search on product name. |
| `categoryId` | Only products in this category. |
| `storeId` / `storeSlug` | Only products from this store. |
| `minPrice` / `maxPrice` | Price range filter. |
| `sortBy` | Field to sort by (default `createdAt`). |
| `sortOrder` | `asc` or `desc` (default `desc`). |
| `pageNumber` | Page to return (default 1). |
| `pageSize` | Items per page (default 10). |

## What a product looks like
Each product includes: `id`, `name`, `description`, `price`, `sku`, `imageUrl`,
`categoryId` + `categoryName`, `storeId` + `storeName` + `storeSlug`, `stockQuantity`,
`averageRating`, `reviewCount`, and `createdAt`.

## Notes
- Products from stores that are Pending, Suspended, or Rejected are **not** shown here.
- Stock quantity is visible so shoppers can see availability before adding to the cart.
