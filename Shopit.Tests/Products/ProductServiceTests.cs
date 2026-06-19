using FluentAssertions;
using Moq;
using Shopit.Application.AI;
using Shopit.Application.Common;
using Shopit.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OfficeOpenXml;
using Shopit.Application.Products.DTOs;
using Shopit.Application.Products.Validators;
using Shopit.Domain.Entities;
using Shopit.Domain.Exceptions;
using Shopit.Infrastructure.Data;
using Shopit.Infrastructure.Services;

namespace Shopit.Tests.Products;

public class ProductServiceTests
{
    public ProductServiceTests()
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
    }

    [Fact]
    public async Task CreateProduct_ValidRequest_ReturnsProductResponse()
    {
        await using var context = CreateContext();
        await SeedCategoryAsync(context);

        var service = CreateService(context);

        var request = new CreateProductRequest
        {
            Name = "Gaming Laptop",
            Description = "Powerful gaming laptop",
            Price = 1299.99m,
            Sku = "LAPTOP-001",
            ImageUrl = "https://example.com/laptop.png",
            CategoryId = 1,
            InitialStock = 15
        };

        var result = await service.CreateAsync(request);

        result.Should().NotBeNull();
        result.Name.Should().Be("Gaming Laptop");
        result.Price.Should().Be(1299.99m);
        result.Sku.Should().Be("LAPTOP-001");
        result.CategoryId.Should().Be(1);
        result.CategoryName.Should().Be("Electronics");
        result.StockQuantity.Should().Be(15);
    }

    [Fact]
    public async Task CreateProduct_DuplicateSKU_ThrowsConflictException()
    {
        await using var context = CreateContext();
        await SeedCategoryAsync(context);

        context.Products.Add(new Product
        {
            Id = 10,
            Name = "Existing Product",
            Description = "Already exists",
            Price = 99.99m,
            SKU = "DUPLICATE-SKU",
            CategoryId = 1,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        });

        await context.SaveChangesAsync();

        var service = CreateService(context);

        var request = new CreateProductRequest
        {
            Name = "New Product",
            Description = "Should fail",
            Price = 149.99m,
            Sku = "DUPLICATE-SKU",
            CategoryId = 1,
            InitialStock = 5
        };

        Func<Task> act = async () => await service.CreateAsync(request);

        await act.Should()
            .ThrowAsync<ConflictException>()
            .WithMessage("*Product SKU 'DUPLICATE-SKU' already exists*");
    }

    [Fact]
    public async Task GetProductById_ExistingId_ReturnsProductResponse()
    {
        await using var context = CreateContext();

        var category = new Category
        {
            Id = 1,
            Name = "Electronics",
            Slug = "electronics"
        };

        var product = new Product
        {
            Id = 20,
            Name = "Wireless Mouse",
            Description = "Bluetooth mouse",
            Price = 29.99m,
            SKU = "MOUSE-001",
            ImageUrl = "https://example.com/mouse.png",
            CategoryId = 1,
            Category = category,
            CreatedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            IsDeleted = false,
            Inventory = new Inventory
            {
                Id = 30,
                Quantity = 40,
                UpdatedAt = DateTime.UtcNow,
                RowVersion = new byte[8]
            }
        };

        context.Categories.Add(category);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        var result = await service.GetByIdAsync(20);

        result.Should().NotBeNull();
        result.Id.Should().Be(20);
        result.Name.Should().Be("Wireless Mouse");
        result.Description.Should().Be("Bluetooth mouse");
        result.Price.Should().Be(29.99m);
        result.Sku.Should().Be("MOUSE-001");
        result.ImageUrl.Should().Be("https://example.com/mouse.png");
        result.CategoryId.Should().Be(1);
        result.CategoryName.Should().Be("Electronics");
        result.StockQuantity.Should().Be(40);
        result.AverageRating.Should().Be(0);
        result.ReviewCount.Should().Be(0);
        result.CreatedAt.Should().Be(new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task GetProductById_NotFound_ThrowsNotFoundException()
    {
        await using var context = CreateContext();
        await SeedCategoryAsync(context);

        var service = CreateService(context);

        Func<Task> act = async () => await service.GetByIdAsync(999);

        await act.Should()
            .ThrowAsync<NotFoundException>()
            .WithMessage("*Product with id 999 was not found*");
    }

    [Fact]
    public async Task DeleteProduct_ExistingProduct_SetsIsDeletedTrue()
    {
        await using var context = CreateSpyContext();

        var category = new Category
        {
            Id = 1,
            Name = "Electronics",
            Slug = "electronics"
        };

        var product = new Product
        {
            Id = 50,
            Name = "Keyboard",
            Description = "Mechanical keyboard",
            Price = 79.99m,
            SKU = "KEYBOARD-001",
            CategoryId = 1,
            Category = category,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        };

        context.Categories.Add(category);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        context.ProductIdToWatch = 50;

        var service = CreateService(context);

        await service.DeleteAsync(50);

        var deletedProduct = await context.Products.FirstAsync(p => p.Id == 50);

        deletedProduct.IsDeleted.Should().BeTrue();
        context.ProductWasDeletedWhenSaveChangesStarted.Should().BeTrue();
    }

    [Fact]
    public async Task ImportExcel_ValidRows_ReturnsCorrectAddedCount()
    {
        await using var context = CreateContext();
        await SeedCategoryAsync(context);

        var service = CreateService(context);

        await using var stream = CreateExcelStream(
            new object?[] { "Product 1", "SKU-001", 10.99m, "Electronics", "Description 1", 5 },
            new object?[] { "Product 2", "SKU-002", 20.99m, "Electronics", "Description 2", 10 },
            new object?[] { "Product 3", "SKU-003", 30.99m, "Electronics", "Description 3", 15 },
            new object?[] { "Product 4", "SKU-004", 40.99m, "Electronics", "Description 4", 20 },
            new object?[] { "Product 5", "SKU-005", 50.99m, "Electronics", "Description 5", 25 }
        );

        var result = await service.ImportAsync(stream);

        result.Should().NotBeNull();
        result.AddedCount.Should().Be(5);
        result.FailedCount.Should().Be(0);
        result.Errors.Should().BeEmpty();

        var productsInDatabase = await context.Products.CountAsync();

        productsInDatabase.Should().Be(5);
    }

    [Fact]
    public async Task ImportExcel_InvalidPrice_IncludesRowInFailedList()
    {
        await using var context = CreateContext();
        await SeedCategoryAsync(context);

        var service = CreateService(context);

        await using var stream = CreateExcelStream(
            new object?[] { "Invalid Product", "BAD-PRICE-001", "not-a-number", "Electronics", "Invalid price row", 5 }
        );

        var result = await service.ImportAsync(stream);

        result.Should().NotBeNull();
        result.AddedCount.Should().Be(0);
        result.FailedCount.Should().Be(1);
        result.Errors.Should().ContainSingle();

        var error = result.Errors.Single();

        error.Row.Should().Be(2);
        error.Reason.Should().Contain("Price must be a valid number greater than 0.");
    }

    [Fact]
    public async Task GenerateContent_ExistingProduct_DelegatesToGeminiWithProductData()
    {
        await using var context = CreateContext();

        var category = new Category { Id = 1, Name = "Phone Accessories", Slug = "phone-accessories" };
        context.Categories.Add(category);
        context.Products.Add(new Product
        {
            Id = 70,
            Name = "iPhone 15 Pro Case",
            Description = "MagSafe compatible, silicone, shockproof",
            Price = 19.99m,
            SKU = "CASE-001",
            CategoryId = 1,
            Category = category,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        });
        await context.SaveChangesAsync();

        var expected = new ProductContentResponse
        {
            Description = "A great case.",
            Features = new List<string> { "1", "2", "3", "4", "5" },
            SeoTitle = "iPhone 15 Pro Case",
            MetaDescription = "Protect your phone."
        };

        var geminiMock = new Mock<IGeminiService>();
        geminiMock
            .Setup(g => g.GenerateProductContentAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var service = CreateService(context, geminiMock.Object);

        var result = await service.GenerateContentAsync(70);

        result.Should().BeSameAs(expected);
        geminiMock.Verify(g => g.GenerateProductContentAsync(
            "iPhone 15 Pro Case",
            "Phone Accessories",
            "MagSafe compatible, silicone, shockproof",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateContent_NotFound_ThrowsNotFoundException()
    {
        await using var context = CreateContext();
        await SeedCategoryAsync(context);

        var geminiMock = new Mock<IGeminiService>();
        var service = CreateService(context, geminiMock.Object);

        Func<Task> act = async () => await service.GenerateContentAsync(999);

        await act.Should()
            .ThrowAsync<NotFoundException>()
            .WithMessage("*Product with id 999 was not found*");

        geminiMock.Verify(g => g.GenerateProductContentAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ProductService CreateService(AppDbContext context, IGeminiService? geminiService = null)
    {
        var cacheMock = new Mock<ICacheService>();

        cacheMock
            .Setup(c => c.GetAsync<PaginatedResult<ProductResponse>>(It.IsAny<string>()))
            .ReturnsAsync((PaginatedResult<ProductResponse>?)null);

        cacheMock
            .Setup(c => c.GetAsync<ProductResponse>(It.IsAny<string>()))
            .ReturnsAsync((ProductResponse?)null);

        cacheMock
            .Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<PaginatedResult<ProductResponse>>(), It.IsAny<TimeSpan>()))
            .Returns(Task.CompletedTask);

        cacheMock
            .Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<ProductResponse>(), It.IsAny<TimeSpan>()))
            .Returns(Task.CompletedTask);

        cacheMock
            .Setup(c => c.RemoveAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        cacheMock
            .Setup(c => c.RemoveByPatternAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        return new ProductService(
            context,
            new CreateProductRequestValidator(),
            new UpdateProductRequestValidator(),
            cacheMock.Object,
            geminiService ?? Mock.Of<IGeminiService>());
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"ShopitTests-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new AppDbContext(options);
    }

    private static SaveChangesSpyDbContext CreateSpyContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"ShopitTests-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new SaveChangesSpyDbContext(options);
    }

    private static async Task SeedCategoryAsync(AppDbContext context)
    {
        context.Categories.Add(new Category
        {
            Id = 1,
            Name = "Electronics",
            Slug = "electronics"
        });

        await context.SaveChangesAsync();
    }

    private static MemoryStream CreateExcelStream(params object?[][] rows)
    {
        var stream = new MemoryStream();

        using var package = new ExcelPackage();

        var worksheet = package.Workbook.Worksheets.Add("Products");

        worksheet.Cells[1, 1].Value = "Name";
        worksheet.Cells[1, 2].Value = "SKU";
        worksheet.Cells[1, 3].Value = "Price";
        worksheet.Cells[1, 4].Value = "CategoryName";
        worksheet.Cells[1, 5].Value = "Description";
        worksheet.Cells[1, 6].Value = "InitialStock";

        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            var row = rows[rowIndex];

            for (var columnIndex = 0; columnIndex < row.Length; columnIndex++)
            {
                worksheet.Cells[rowIndex + 2, columnIndex + 1].Value = row[columnIndex];
            }
        }

        package.SaveAs(stream);
        stream.Position = 0;

        return stream;
    }

    private sealed class SaveChangesSpyDbContext : AppDbContext
    {
        public SaveChangesSpyDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public int? ProductIdToWatch { get; set; }

        public bool? ProductWasDeletedWhenSaveChangesStarted { get; private set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (ProductIdToWatch.HasValue)
            {
                var productEntry = ChangeTracker
                    .Entries<Product>()
                    .FirstOrDefault(entry => entry.Entity.Id == ProductIdToWatch.Value);

                if (productEntry is not null)
                {
                    ProductWasDeletedWhenSaveChangesStarted = productEntry.Entity.IsDeleted;
                }
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}