# Product Categories

## What it does
Categories organize the Shopit catalog into groups (for example "Electronics" or
"Books"). Shoppers use categories to browse a section of the catalog or to filter the
product list down to one category.

## Who can use it
Anyone can read the list of categories and view a single category — no login required.
(Creating, editing, and deleting categories is an administrator task and is not part of
the shopping experience.)

## How it works
- Fetch the full list of categories to show a navigation menu or a filter.
- Each category has an `id` and a `name`.
- To see the products in a category, call the product list with the `categoryId` filter
  (see [Browsing and Searching Products](browse-products.md)).

## Endpoints
| Method | Route | Description |
|---|---|---|
| GET | `/api/v1/categories` | List all categories. |
| GET | `/api/v1/categories/{id}` | Get a single category by ID. |

## Notes
- Categories are shared across all stores; a product belongs to exactly one category.
