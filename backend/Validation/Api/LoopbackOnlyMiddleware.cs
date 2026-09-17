using System.Net;
using Microsoft.Extensions.Options;

namespace Validation.Api;

// Loopback-only by default. Setting AllowNonLoopback=true removes this API's only network
// access control; it is unsafe unless the deployment adds its own authentication,
// authorization, and network isolation in front of it. This API implements none of those
// itself and must not be exposed to non-loopback callers without them.
public sealed class LoopbackOnlyMiddleware(RequestDelegate next, IOptions<ValidationApiOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!options.Value.AllowNonLoopback &&
            context.Connection.RemoteIpAddress is { } remote &&
            !IPAddress.IsLoopback(remote))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await Results.Problem("Validation API accepts loopback requests only by default.", statusCode: StatusCodes.Status403Forbidden)
                .ExecuteAsync(context);
            return;
        }
        await next(context);
    }
}

// Endpoint filter form: same policy, scoped to a MapGroup(...) so it doesn't block
// non-validation endpoints in a composed host.
public sealed class LoopbackOnlyEndpointFilter(IOptions<ValidationApiOptions> options) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (!options.Value.AllowNonLoopback &&
            http.Connection.RemoteIpAddress is { } remote &&
            !IPAddress.IsLoopback(remote))
        {
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Results.Problem("Validation API accepts loopback requests only by default.", statusCode: StatusCodes.Status403Forbidden);
        }
        return await next(context);
    }
}
