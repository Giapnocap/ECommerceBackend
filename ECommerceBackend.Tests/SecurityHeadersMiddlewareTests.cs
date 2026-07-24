using ECommerceBackend.API.Middlewares;
using Microsoft.AspNetCore.Http;

namespace ECommerceBackend.Tests;

public sealed class SecurityHeadersMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_AddsSecurityHeadersBeforeInvokingNextMiddleware()
    {
        var context = new DefaultHttpContext();
        var middleware = new SecurityHeadersMiddleware(nextContext =>
        {
            Assert.Equal("nosniff", nextContext.Response.Headers["X-Content-Type-Options"]);
            Assert.Equal("DENY", nextContext.Response.Headers["X-Frame-Options"]);
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.Equal("no-referrer", context.Response.Headers["Referrer-Policy"]);
        Assert.Equal(
            "camera=(), geolocation=(), microphone=()",
            context.Response.Headers["Permissions-Policy"]);
    }
}
