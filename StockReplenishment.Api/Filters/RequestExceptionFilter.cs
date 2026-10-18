using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using StockReplenishment.Application;

namespace StockReplenishment.Api.Filters;

/// <summary>Translates workflow errors from the application layer into Problem Details responses.</summary>
public class RequestExceptionFilter : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not RequestException exception)
            return;

        var statusCode = exception.Error switch
        {
            RequestError.Validation => StatusCodes.Status400BadRequest,
            RequestError.NotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status409Conflict
        };

        var problem = new ProblemDetails { Status = statusCode, Title = exception.Message };
        context.Result = new ObjectResult(problem) { StatusCode = statusCode };
        context.ExceptionHandled = true;
    }
}
