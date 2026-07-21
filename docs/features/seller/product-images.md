# Managing Product Images

## What it does
Lets a seller upload or remove the image shown for one of their products. Images are
stored in cloud blob storage and the product keeps a URL pointing to the uploaded image.

## Who can use it
Signed-in users with the **Seller** role (for their own products) or **Admin**. Acting on
a product you don't own is forbidden.

## How it works
- Upload an image by sending a single image file as `multipart/form-data`.
- Accepted formats: **.jpg**, **.jpeg**, **.png**. Any other extension is rejected.
- Maximum size: **5 MB**. Larger files are rejected.
- On success the image is stored and the product's image URL is returned
  (`{ "imageUrl": "..." }`).
- Deleting the image removes it from storage and clears it from the product.

## Endpoints
| Method | Route | Description |
|---|---|---|
| POST | `/api/v1/products/{id}/image` | Upload/replace the product image (multipart file). |
| DELETE | `/api/v1/products/{id}/image` | Remove the product's image. |

## Notes
- Images live in the `product-images` blob container.
- You can also set an `imageUrl` directly when creating or updating a product if the image
  is already hosted elsewhere — see [Managing Products](manage-products.md).
