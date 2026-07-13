# Product Reviews and Ratings

## What it does
Lets customers rate a product from 1 to 5 stars and leave an optional written comment.
Anyone can read a product's reviews and see its average rating.

## Who can use it
- **Reading** reviews for a product is public — no login required.
- **Writing, editing, or deleting** a review requires being signed in, and you can only
  review a product you have actually received.

## How it works
- Reading a product's reviews returns the product's `averageRating`, the total review
  count, and a paginated list of reviews (each with the reviewer's first/last name,
  rating, comment, and date).
- To submit a review you provide the `productId`, a `rating` (1–5), and an optional
  `comment` (up to 1000 characters).
- Business rules enforced when submitting:
  - You can **only review a product you have received** — otherwise it's forbidden.
  - You can review a given product **once**; a second review is rejected (409 Conflict).
- Editing your review is only allowed **within 48 hours** of submitting it, and only by
  the review's author.
- You can delete your own review at any time. (Admins can delete any review as
  moderation.)

## Endpoints
| Method | Route | Description |
|---|---|---|
| GET | `/api/v1/reviews/product/{productId}` | Read a product's reviews (paginated). |
| POST | `/api/v1/reviews` | Submit a review (`productId`, `rating`, `comment`). |
| PUT | `/api/v1/reviews/{reviewId}` | Edit your review (within 48 hours). |
| DELETE | `/api/v1/reviews/{reviewId}` | Delete your own review. |

## Notes
- A product's `averageRating` and `reviewCount` shown in the catalog come from its
  reviews.
- Ratings must be between 1 and 5; comments are optional but capped at 1000 characters.
