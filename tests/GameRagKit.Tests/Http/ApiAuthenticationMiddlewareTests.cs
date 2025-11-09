using FluentAssertions;
using GameRagKit.Http;
using Microsoft.AspNetCore.Http;

namespace GameRagKit.Tests.Http;

public sealed class ApiAuthenticationMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_Allows_Options_Preflight()
    {
        var options = new ApiAuthOptions(
            new HashSet<string> { "key" },
            new HashSet<string>(),
            new HashSet<string>());

        var invoked = false;
        RequestDelegate next = context =>
        {
            invoked = true;
            return Task.CompletedTask;
        };

        var middleware = new ApiAuthenticationMiddleware(next, options);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Options;

        await middleware.InvokeAsync(context);

        invoked.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }
}
