using FluentAssertions;
using Moq;
using Shopit.Application.DTOs.Categories;
using Shopit.Application.Interfaces;
using Shopit.Domain.Entities;
using Shopit.Domain.Exceptions;
using Shopit.Infrastructure.Repositories;

namespace Shopit.Tests;

public class CategoryServiceTests
{
    private readonly Mock<ICategoryRepository> _repositoryMock;
    private readonly CategoryService _service;

    public CategoryServiceTests()
    {
        _repositoryMock = new Mock<ICategoryRepository>();
        _service = new CategoryService(_repositoryMock.Object);
    }

    // Test 1
    [Fact]
    public async Task CreateCategory_ValidRequest_ReturnsCategory()
    {
        // Arrange
        var request = new CreateCategoryRequest { Name = "Electronics" };

        _repositoryMock
            .Setup(r => r.GetByNameAsync("Electronics"))
            .ReturnsAsync((Category?)null);

        _repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<Category>()))
            .Returns(Task.CompletedTask);

        _repositoryMock
            .Setup(r => r.SaveChangesAsync())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("Electronics");
    }

    // Test 2
    [Fact]
    public async Task CreateCategory_DuplicateName_ThrowsConflictException()
    {
        // Arrange
        var request = new CreateCategoryRequest { Name = "Electronics" };
        var existingCategory = new Category { Id = 1, Name = "Electronics" };

        _repositoryMock
            .Setup(r => r.GetByNameAsync("Electronics"))
            .ReturnsAsync(existingCategory);

        // Act
        var act = async () => await _service.CreateAsync(request);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*Electronics*");
    }

    // Test 3
    [Fact]
    public async Task GetCategoryById_ExistingId_ReturnsCategoryWithSubcategories()
    {
        // Arrange
        var category = new Category
        {
            Id = 1,
            Name = "Electronics",
            Slug = "electronics",
            SubCategories = new List<Category>
            {
                new() { Id = 2, Name = "Phones", Slug = "phones" },
                new() { Id = 3, Name = "Laptops", Slug = "laptops" }
            }
        };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(category);

        // Act
        var result = await _service.GetByIdAsync(1);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("Electronics");
        result.Subcategories.Should().HaveCount(2);
        result.SubcategoryCount.Should().Be(2);
    }

    // Test 4
    [Fact]
    public async Task GetCategoryById_NotFound_ThrowsNotFoundException()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetByIdAsync(999))
            .ReturnsAsync((Category?)null);

        // Act
        var act = async () => await _service.GetByIdAsync(999);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*999*");
    }

    // Test 5
    [Fact]
    public async Task DeleteCategory_WithLinkedProducts_ThrowsConflictException()
    {
        // Arrange
        var category = new Category { Id = 1, Name = "Electronics", Slug = "electronics" };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(category);

        _repositoryMock
            .Setup(r => r.GetProductCountAsync(1))
            .ReturnsAsync(5);

        // Act
        var act = async () => await _service.DeleteAsync(1);

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*Remove the products first*");
    }

    // Test 6
    [Fact]
    public async Task DeleteCategory_NoLinkedProducts_DeletesSuccessfully()
    {
        // Arrange
        var category = new Category { Id = 1, Name = "Electronics", Slug = "electronics" };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(category);

        _repositoryMock
            .Setup(r => r.GetProductCountAsync(1))
            .ReturnsAsync(0);

        _repositoryMock
            .Setup(r => r.DeleteAsync(category))
            .Returns(Task.CompletedTask);

        _repositoryMock
            .Setup(r => r.SaveChangesAsync())
            .Returns(Task.CompletedTask);

        // Act
        await _service.DeleteAsync(1);

        // Assert
        _repositoryMock.Verify(r => r.DeleteAsync(category), Times.Once);
        _repositoryMock.Verify(r => r.SaveChangesAsync(), Times.Once);
    }
}