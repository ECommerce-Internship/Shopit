using Azure.Storage.Queues;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using Shopit.Application.Interfaces;
using Shopit.Infrastructure.Data;
using Shopit.Infrastructure.Repositories;
using Shopit.Infrastructure.Services;
using StackExchange.Redis;
using Shopit.Application.Products;
using Microsoft.Extensions.Logging;
using FluentValidation;
using Shopit.Application.Products.DTOs;
using Shopit.Application.AI;
using Pgvector.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

// Configuration
var config = builder.Configuration;

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(config["ConnectionStrings:DefaultConnection"],
        o => o.UseVector()));

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(config["ConnectionStrings:Redis"]!));

builder.Services.AddSingleton(new QueueClient(
    config["ConnectionStrings:AzureQueue"],
    config["AzureQueue:QueueName"]));

// Services
builder.Services.AddValidatorsFromAssemblyContaining<CreateProductRequest>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<ICacheService, CacheService>();
builder.Services.AddScoped<ILowStockAlertService, LowStockAlertService>();
builder.Services.AddScoped<IEmailService, EmailServiceStub>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IGeminiService, GeminiService>();



builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});


// MCP Server
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var app = builder.Build();
await app.RunAsync();