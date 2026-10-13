using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ApolloData.Api.Common;

/// <summary>Consistent RFC 7807 responses with Czech, user-facing messages.</summary>
public static class ApiProblems
{
    public static IResult Validation(ValidationErrors errors) =>
        TypedResults.ValidationProblem(errors.ToDictionary(), title: "Formulář obsahuje chyby.");

    public static IResult Validation(string field, string message)
    {
        var errors = new ValidationErrors();
        errors.Add(field, message);
        return Validation(errors);
    }

    public static IResult BadRequest(string detail, string code) =>
        TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest, title: "Neplatný požadavek.",
            extensions: new Dictionary<string, object?> { ["code"] = code });

    public static IResult Conflict(string detail, string code, string? field = null)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = code };
        if (field is not null) extensions["errors"] = new Dictionary<string, string[]> { [field] = [detail] };
        return TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status409Conflict, title: "Konflikt.", extensions: extensions);
    }

    public static IResult NotFound(string detail = "Záznam nebyl nalezen.") =>
        TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status404NotFound, title: "Nenalezeno.");

    public static IResult Unauthorized(string detail) =>
        TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status401Unauthorized, title: "Nepřihlášeno.");

    public static IResult ServiceUnavailable(string detail, string code) =>
        TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status503ServiceUnavailable, title: "Služba je dočasně nedostupná.",
            extensions: new Dictionary<string, object?> { ["code"] = code });
}

/// <summary>
/// Centralized exception handling. Production responses never contain exception details;
/// in Development the exception message is added to help debugging.
/// </summary>
internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            // Client went away; nothing useful to send.
            return true;
        }

        var (status, title, detail) = exception switch
        {
            BadHttpRequestException bad => (bad.StatusCode, "Neplatný požadavek.", "Požadavek nemá správný formát."),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Konflikt.",
                "Záznam byl mezitím změněn. Načtěte prosím aktuální data a akci opakujte."),
            _ => (StatusCodes.Status500InternalServerError, "Došlo k neočekávané chybě.",
                "Omlouváme se, požadavek se nepodařilo zpracovat. Zkuste to prosím znovu."),
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        }
        else
        {
            logger.LogInformation("Request {Method} {Path} failed with {StatusCode}: {ExceptionType}",
                context.Request.Method, context.Request.Path, status, exception.GetType().Name);
        }

        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        if (environment.IsDevelopment())
        {
            problem.Extensions["exception"] = $"{exception.GetType().Name}: {exception.Message}";
        }

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
