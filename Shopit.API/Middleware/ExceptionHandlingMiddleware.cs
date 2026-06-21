using Shopit.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Net;

namespace Shopit.API.Middleware;
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;


    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning(ex, "Resource not found");
            await HandleExceptionAsync(context, ex);
        }
        catch (ConflictException ex)
        {
            _logger.LogWarning(ex, "Conflict occurred");
            await HandleExceptionAsync(context, ex);
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning(ex, "Forbidden access");
            await HandleExceptionAsync(context, ex);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning(ex, "Validation error");
            await HandleExceptionAsync(context, ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error occurred");
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, title) = exception switch
        {
            NotFoundException => (404, "Not Found"),
            ConflictException => (409, "Conflict"),
            ValidationException => (400, "Bad Request"),
            UnauthorizedException => (401, "Unauthorized"),
            ForbiddenException => (403, "Forbidden"),
            ExternalServiceException => (502, "Bad Gateway"),
            _ => (500, "Internal Server Error")
        };

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = statusCode == 500 ? "An unexpected error occurred." : exception.Message
        };

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(problemDetails));
    }
}