using Shopit.Application.Products.DTOs;

namespace Shopit.Application.DTOs.ImageSearch;

/// <summary>
/// A single visual-search hit: the matched product plus its similarity score
/// against the query image (1.0 = visually identical direction, ~0 = unrelated).
/// </summary>
public record ImageSearchMatchResponse(ProductResponse Product, double Score);
