using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shopit.Application.DTOs.Categories;
using Shopit.Application.Interfaces;

namespace Shopit.API.Controllers;

[ApiController]
[Route("api/v1/categories")]
public class CategoryController : ControllerBase
{
    private readonly ICategoryService _categoryService;

    public CategoryController(ICategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var categories = await _categoryService.GetAllAsync();
        return Ok(categories);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var category = await _categoryService.GetByIdAsync(id);
        return Ok(category);
    }

    [HttpPost]
    // [Authorize(Roles = "Admin")] -- uncomment when JWT auth is set up
    public async Task<IActionResult> Create([FromBody] CreateCategoryRequest request)
    {
        var category = await _categoryService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = category.Id }, category);
    }

    [HttpPut("{id}")]
    // [Authorize(Roles = "Admin")] -- uncomment when JWT auth is set up
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCategoryRequest request)
    {
        var category = await _categoryService.UpdateAsync(id, request);
        return Ok(category);
    }

    [HttpDelete("{id}")]
    // [Authorize(Roles = "Admin")] -- uncomment when JWT auth is set up
    public async Task<IActionResult> Delete(int id)
    {
        await _categoryService.DeleteAsync(id);
        return NoContent();
    }
}