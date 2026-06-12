using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using Shopit.Application.Common;
using Shopit.Application.Products;
using Shopit.Application.Products.DTOs;
using Shopit.Domain.Entities;
using Shopit.Domain.Exceptions;
using Shopit.Infrastructure.Data;
using DomainValidationException = Shopit.Domain.Exceptions.ValidationException;

namespace Shopit.Infrastructure.Services;

public class ProductService : IProductService
{
    private readonly AppDbContext _context;
    private readonly IValidator<CreateProductRequest> _createValidator;
    private readonly IValidator<UpdateProductRequest> _updateValidator;

    public ProductService(
        AppDbContext context,
        IValidator<CreateProductRequest> createValidator,
        IValidator<UpdateProductRequest> updateValidator)
    {
        _context = context;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    public async Task<PaginatedResult<ProductResponse>> GetAllAsync(ProductQueryParameters queryParameters)
    {
        queryParameters ??= new ProductQueryParameters();

        var pageNumber = queryParameters.PageNumber <= 0 ? 1 : queryParameters.PageNumber;
        var pageSize = queryParameters.PageSize <= 0 ? 10 : queryParameters.PageSize;

        if (pageSize > 100)
            pageSize = 100;

        if (queryParameters.MinPrice.HasValue &&
            queryParameters.MaxPrice.HasValue &&
            queryParameters.MinPrice.Value > queryParameters.MaxPrice.Value)
        {
            throw new DomainValidationException("MinPrice cannot be greater than MaxPrice.");
        }

        var productsQuery = _context.Products
            .AsNoTracking()
            .Where(p => !p.IsDeleted);

        if (!string.IsNullOrWhiteSpace(queryParameters.Search))
        {
            var searchTerm = queryParameters.Search.Trim();
            productsQuery = productsQuery.Where(p => p.Name.Contains(searchTerm));
        }

        if (queryParameters.CategoryId.HasValue)
        {
            productsQuery = productsQuery.Where(p => p.CategoryId == queryParameters.CategoryId.Value);
        }

        if (queryParameters.MinPrice.HasValue)
        {
            productsQuery = productsQuery.Where(p => p.Price >= queryParameters.MinPrice.Value);
        }

        if (queryParameters.MaxPrice.HasValue)
        {
            productsQuery = productsQuery.Where(p => p.Price <= queryParameters.MaxPrice.Value);
        }

        var sortBy = (queryParameters.SortBy ?? "createdAt").Trim().ToLowerInvariant();
        var sortDirection = (queryParameters.SortDirection ?? "desc").Trim().ToLowerInvariant();

        if (sortDirection != "asc" && sortDirection != "desc")
        {
            throw new DomainValidationException("SortDirection must be either 'asc' or 'desc'.");
        }

        var descending = sortDirection == "desc";

        productsQuery = sortBy switch
        {
            "name" => descending
                ? productsQuery.OrderByDescending(p => p.Name)
                : productsQuery.OrderBy(p => p.Name),

            "price" => descending
                ? productsQuery.OrderByDescending(p => p.Price)
                : productsQuery.OrderBy(p => p.Price),

            "createdat" => descending
                ? productsQuery.OrderByDescending(p => p.CreatedAt)
                : productsQuery.OrderBy(p => p.CreatedAt),

            _ => throw new DomainValidationException("SortBy must be one of: name, price, createdAt.")
        };

        var totalCount = await productsQuery.CountAsync();

        var items = await ProjectToResponse(productsQuery)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PaginatedResult<ProductResponse>(
            items,
            totalCount,
            pageNumber,
            pageSize);
    }

    public async Task<ProductResponse> GetByIdAsync(int id)
    {
        var product = await ProjectToResponse(
                _context.Products
                    .AsNoTracking()
                    .Where(p => p.Id == id && !p.IsDeleted))
            .FirstOrDefaultAsync();

        if (product is null)
        {
            throw new NotFoundException($"Product with id {id} was not found.");
        }

        return product;
    }

    public async Task<ProductResponse> CreateAsync(CreateProductRequest request)
    {
        var validationResult = await _createValidator.ValidateAsync(request);

        if (!validationResult.IsValid)
        {
            throw new DomainValidationException(CombineValidationErrors(validationResult));
        }

        var sku = request.Sku.Trim();

        var categoryExists = await _context.Categories
            .AnyAsync(c => c.Id == request.CategoryId);

        if (!categoryExists)
        {
            throw new NotFoundException($"Category with id {request.CategoryId} was not found.");
        }

        var skuExists = await _context.Products
            .AnyAsync(p => p.SKU == sku);

        if (skuExists)
        {
            throw new ConflictException($"Product SKU '{sku}' already exists.");
        }

        var product = new Product
        {
            Name = request.Name.Trim(),
            Description = request.Description,
            Price = request.Price,
            SKU = sku,
            ImageUrl = request.ImageUrl,
            CategoryId = request.CategoryId,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false,
            Inventory = new Inventory
            {
                Quantity = request.InitialStock,
                UpdatedAt = DateTime.UtcNow
            }
        };

        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(product.Id);
    }

    public async Task<ProductResponse> UpdateAsync(int id, UpdateProductRequest request)
    {
        var validationResult = await _updateValidator.ValidateAsync(request);

        if (!validationResult.IsValid)
        {
            throw new DomainValidationException(CombineValidationErrors(validationResult));
        }

        var product = await _context.Products
            .Include(p => p.Inventory)
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);

        if (product is null)
        {
            throw new NotFoundException($"Product with id {id} was not found.");
        }

        var sku = request.Sku.Trim();

        var categoryExists = await _context.Categories
            .AnyAsync(c => c.Id == request.CategoryId);

        if (!categoryExists)
        {
            throw new NotFoundException($"Category with id {request.CategoryId} was not found.");
        }

        var skuExists = await _context.Products
            .AnyAsync(p => p.SKU == sku && p.Id != id);

        if (skuExists)
        {
            throw new ConflictException($"Product SKU '{sku}' already exists.");
        }

        product.Name = request.Name.Trim();
        product.Description = request.Description;
        product.Price = request.Price;
        product.SKU = sku;
        product.ImageUrl = request.ImageUrl;
        product.CategoryId = request.CategoryId;

        if (product.Inventory is null)
        {
            product.Inventory = new Inventory
            {
                ProductId = product.Id,
                Quantity = request.StockQuantity,
                UpdatedAt = DateTime.UtcNow
            };
        }
        else
        {
            product.Inventory.Quantity = request.StockQuantity;
            product.Inventory.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        return await GetByIdAsync(id);
    }

    public async Task DeleteAsync(int id)
    {
        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);

        if (product is null)
        {
            throw new NotFoundException($"Product with id {id} was not found.");
        }

        product.IsDeleted = true;

        await _context.SaveChangesAsync();
    }
    public async Task<ImportResultDto> ImportAsync(
    Stream fileStream,
    CancellationToken cancellationToken = default)
{
    using var package = new ExcelPackage();

    package.Load(fileStream);

    var worksheet = package.Workbook.Worksheets.FirstOrDefault();

    if (worksheet?.Dimension is null)
    {
        throw new DomainValidationException("The Excel file is empty.");
    }

    ValidateImportHeaders(worksheet);

    var lastRow = worksheet.Dimension.End.Row;

    if (lastRow < 2)
    {
        throw new DomainValidationException("The Excel file must contain at least one product row.");
    }

    var categoryRows = await _context.Categories
        .AsNoTracking()
        .Select(c => new { c.Id, c.Name })
        .ToListAsync(cancellationToken);

    var categoriesByName = categoryRows
        .GroupBy(c => NormalizeKey(c.Name))
        .ToDictionary(g => g.Key, g => g.First().Id);

    var existingSkuList = await _context.Products
        .AsNoTracking()
        .Select(p => p.SKU)
        .ToListAsync(cancellationToken);

    var existingSkus = new HashSet<string>(
        existingSkuList,
        StringComparer.OrdinalIgnoreCase);

    var validSkusInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    var productsToAdd = new List<Product>();
    var inventoriesToAdd = new List<Inventory>();

    var result = new ImportResultDto();

    var now = DateTime.UtcNow;

    for (var row = 2; row <= lastRow; row++)
    {
        if (IsRowEmpty(worksheet, row))
        {
            continue;
        }

        var rowErrors = new List<string>();

        var name = GetCellText(worksheet, row, 1);
        var sku = GetCellText(worksheet, row, 2);
        var categoryName = GetCellText(worksheet, row, 4);
        var description = GetCellText(worksheet, row, 5);

        if (string.IsNullOrWhiteSpace(name))
        {
            rowErrors.Add("Name is required.");
        }

        if (string.IsNullOrWhiteSpace(sku))
        {
            rowErrors.Add("SKU is required.");
        }
        else
        {
            if (existingSkus.Contains(sku))
            {
                rowErrors.Add($"SKU '{sku}' already exists.");
            }

            if (validSkusInFile.Contains(sku))
            {
                rowErrors.Add($"SKU '{sku}' is duplicated in this Excel file.");
            }
        }

        if (!TryReadDecimal(worksheet, row, 3, out var price) || price <= 0)
        {
            rowErrors.Add("Price must be a valid number greater than 0.");
        }

        int? categoryId = null;

        if (string.IsNullOrWhiteSpace(categoryName))
        {
            rowErrors.Add("CategoryName is required.");
        }
        else if (categoriesByName.TryGetValue(NormalizeKey(categoryName), out var resolvedCategoryId))
        {
            categoryId = resolvedCategoryId;
        }
        else
        {
            rowErrors.Add($"CategoryName '{categoryName}' does not exist.");
        }

        if (!TryReadInt(worksheet, row, 6, out var initialStock) || initialStock < 0)
        {
            rowErrors.Add("InitialStock must be a whole number greater than or equal to 0.");
        }

        if (rowErrors.Count > 0)
        {
            result.Errors.Add(new ImportErrorDto
            {
                Row = row,
                Reason = string.Join(" | ", rowErrors)
            });

            continue;
        }

        var product = new Product
        {
            Name = name.Trim(),
            SKU = sku.Trim(),
            Price = price,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            CategoryId = categoryId!.Value,
            CreatedAt = now,
            IsDeleted = false
        };

        var inventory = new Inventory
        {
            Product = product,
            Quantity = initialStock,
            UpdatedAt = now
        };

        product.Inventory = inventory;

        productsToAdd.Add(product);
        inventoriesToAdd.Add(inventory);
        validSkusInFile.Add(sku);
    }

    if (productsToAdd.Count > 0)
    {
        _context.Products.AddRange(productsToAdd);
        _context.Inventories.AddRange(inventoriesToAdd);

        await _context.SaveChangesAsync(cancellationToken);
    }

    result.AddedCount = productsToAdd.Count;
    result.FailedCount = result.Errors.Count;

    return result;
}

    private static IQueryable<ProductResponse> ProjectToResponse(IQueryable<Product> query)
    {
        return query.Select(p => new ProductResponse
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            Price = p.Price,
            Sku = p.SKU,
            ImageUrl = p.ImageUrl,
            CategoryId = p.CategoryId,
            CategoryName = p.Category.Name,
            StockQuantity = p.Inventory == null ? 0 : p.Inventory.Quantity,
            AverageRating = p.Reviews.Any()
                ? p.Reviews.Average(r => r.Rating)
                : 0,
            ReviewCount = p.Reviews.Count(),
            CreatedAt = p.CreatedAt
        });
    }

    private static string CombineValidationErrors(FluentValidation.Results.ValidationResult validationResult)
    {
        return string.Join("; ", validationResult.Errors.Select(error => error.ErrorMessage));
    }
    private static void ValidateImportHeaders(ExcelWorksheet worksheet)
{
    var expectedHeaders = new[]
    {
        "Name",
        "SKU",
        "Price",
        "CategoryName",
        "Description",
        "InitialStock"
    };

    for (var column = 1; column <= expectedHeaders.Length; column++)
    {
        var actualHeader = GetCellText(worksheet, 1, column);

        if (!string.Equals(
                actualHeader,
                expectedHeaders[column - 1],
                StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainValidationException(
                $"Invalid Excel template. Column {column} must be '{expectedHeaders[column - 1]}'.");
        }
    }
}

private static bool IsRowEmpty(ExcelWorksheet worksheet, int row)
{
    for (var column = 1; column <= 6; column++)
    {
        if (!string.IsNullOrWhiteSpace(GetCellText(worksheet, row, column)))
        {
            return false;
        }
    }

    return true;
}

private static string GetCellText(ExcelWorksheet worksheet, int row, int column)
{
    return worksheet.Cells[row, column].Text?.Trim() ?? string.Empty;
}

private static bool TryReadDecimal(
    ExcelWorksheet worksheet,
    int row,
    int column,
    out decimal value)
{
    var rawValue = worksheet.Cells[row, column].Value;

    switch (rawValue)
    {
        case decimal decimalValue:
            value = decimalValue;
            return true;

        case double doubleValue:
            value = Convert.ToDecimal(doubleValue);
            return true;

        case int intValue:
            value = intValue;
            return true;

        case long longValue:
            value = longValue;
            return true;
    }

    var text = GetCellText(worksheet, row, column);

    if (decimal.TryParse(
            text,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out value))
    {
        return true;
    }

    return decimal.TryParse(
        text,
        NumberStyles.Number,
        CultureInfo.CurrentCulture,
        out value);
}

private static bool TryReadInt(
    ExcelWorksheet worksheet,
    int row,
    int column,
    out int value)
{
    var rawValue = worksheet.Cells[row, column].Value;

    switch (rawValue)
    {
        case int intValue:
            value = intValue;
            return true;

        case long longValue when longValue >= int.MinValue && longValue <= int.MaxValue:
            value = Convert.ToInt32(longValue);
            return true;

        case double doubleValue when doubleValue % 1 == 0 &&
                                   doubleValue >= int.MinValue &&
                                   doubleValue <= int.MaxValue:
            value = Convert.ToInt32(doubleValue);
            return true;

        case decimal decimalValue when decimalValue % 1 == 0 &&
                                      decimalValue >= int.MinValue &&
                                      decimalValue <= int.MaxValue:
            value = Convert.ToInt32(decimalValue);
            return true;
    }

    var text = GetCellText(worksheet, row, column);

    return int.TryParse(
        text,
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out value);
}

private static string NormalizeKey(string value)
{
    return value.Trim().ToLowerInvariant();
}
}