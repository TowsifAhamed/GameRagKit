using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace GameRagKit.Http;

public sealed class ApiAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ApiAuthOptions _options;

    public ApiAuthenticationMiddleware(RequestDelegate next, ApiAuthOptions options)
    {
        _next = next;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsOptions(context.Request.Method))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (_options.IsAnonymousPath(context.Request.Path))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (!_options.RequiresAuthentication)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (_options.TryValidateApiKey(context.Request.Headers["X-API-Key"]) ||
            _options.TryValidateApiKey(context.Request.Headers["X-GameRAG-ApiKey"]) ||
            _options.TryValidateBearer(context.Request.Headers["Authorization"]))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" }).ConfigureAwait(false);
    }
}
