using Azure.Storage.Queues;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using Shopit.Application.Interfaces;
using Shopit.Infrastructure.Data;
using Shopit.Infrastructure.Repositories;
using Shopit.Infrastructure.Services;
using StackExchange.Redis;
using Shopit.Application.Products;
using FluentValidation;
using Shopit.Application.Products.DTOs;
using Shopit.Application.AI;
using Pgvector.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IGeminiService, GeminiService>();

// SCRUM-153: HTTP (streamable) transport, replacing stdio. The server now
// runs as an independent ASP.NET Core process/container, reachable by the
// API over HTTP rather than being spawned as a child process per request.
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

// Plain health endpoint for the Docker healthcheck (SCRUM-153) - intentionally
// not McpServerHealthCheck/AddHealthChecks() machinery, since all this needs
// to confirm is that the process is up and able to handle HTTP requests at all.
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapMcp();

await app.RunAsync();