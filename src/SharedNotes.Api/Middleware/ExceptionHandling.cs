using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace SharedNotes.Api.Middleware;

/// <summary>An error that maps directly to an HTTP status code and ProblemDetails response.</summary>
public class ApiException(int status, string title, string detail) : Exception(detail)
{
    public int Status { get; } = status;
    public string Title { get; } = title;

    public static ApiException BadRequest(string detail) => new(StatusCodes.Status400BadRequest, "Bad Request", detail);
    public static ApiException Unauthorized(string detail) => new(StatusCodes.Status401Unauthorized, "Unauthorized", detail);
    public static ApiException Forbidden(string detail) => new(StatusCodes.Status403Forbidden, "Forbidden", detail);
    public static ApiException NotFound(string detail) => new(StatusCodes.Status404NotFound, "Not Found", detail);
    public static ApiException Conflict(string detail) => new(StatusCodes.Status409Conflict, "Conflict", detail);
    public static ApiException TooManyRequests(string detail) => new(StatusCodes.Status429TooManyRequests, "Too Many Requests", detail);
}

public static class ProblemWriter
{
    public static Task WriteAsync(HttpContext context, int status, string title, string detail)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        return context.Response.WriteAsJsonAsync(
            new ProblemDetails { Status = status, Title = title, Detail = detail, Instance = context.Request.Path },
            options: null, contentType: "application/problem+json");
    }
}

/// <summary>Turns exceptions into consistent ProblemDetails responses.</summary>
public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ApiException ex)
        {
            await ProblemWriter.WriteAsync(context, ex.Status, ex.Title, ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            await ProblemWriter.WriteAsync(context, StatusCodes.Status409Conflict, "Conflict",
                "The record was modified by someone else. Reload and try again.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
        {
            // Backstop for races on unique / foreign-key constraints that the services' own checks could not see.
            await ProblemWriter.WriteAsync(context, StatusCodes.Status409Conflict, "Conflict",
                "The change conflicts with existing data.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
            await ProblemWriter.WriteAsync(context, StatusCodes.Status500InternalServerError, "Internal Server Error",
                "An unexpected error occurred.");
        }
    }
}
