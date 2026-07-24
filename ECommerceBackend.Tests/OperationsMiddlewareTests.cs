using System.Text.Json;
using ECommerceBackend.API.Errors;
using ECommerceBackend.API.Extensions;
using ECommerceBackend.API.Middlewares;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Tests;

public sealed class OperationsMiddlewareTests
{
    [Fact]
    public async Task CorrelationId_UsesValidRequestHeaderAndReturnsItToCaller()
    {
        const string requestedId = "checkout-20260721.001";
        string? observedTraceId = null;
        var middleware = new CorrelationIdMiddleware(context =>
        {
            observedTraceId = context.TraceIdentifier;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = requestedId;

        await middleware.Invoke(context);

        Assert.Equal(requestedId, observedTraceId);
        Assert.Equal(
            requestedId,
            context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
    }

    [Fact]
    public async Task CorrelationId_ReplacesUnsafeRequestHeader()
    {
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "unsafe correlation id";

        await middleware.Invoke(context);

        Assert.NotEqual("unsafe correlation id", context.TraceIdentifier);
        Assert.Matches("^[a-fA-F0-9]{32}$", context.TraceIdentifier);
        Assert.Equal(
            context.TraceIdentifier,
            context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
    }

    [Fact]
    public async Task ExceptionMiddleware_ClientAbortDoesNotWriteInternalServerError()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var middleware = CreateExceptionMiddleware(_ =>
            Task.FromException(new OperationCanceledException(cancellation.Token)));
        var context = new DefaultHttpContext
        {
            RequestAborted = cancellation.Token
        };
        context.Response.Body = new MemoryStream();

        await middleware.Invoke(context);

        Assert.Equal(499, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Fact]
    public async Task ExceptionMiddleware_ApiErrorKeepsStableCodeAndTraceId()
    {
        var middleware = CreateExceptionMiddleware(_ =>
            Task.FromException(new BusinessException("stable_error", "Invalid operation.")));
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "request-trace-001"
        };
        context.Request.Path = "/api/test";
        context.Response.Body = new MemoryStream();

        await middleware.Invoke(context);
        context.Response.Body.Position = 0;
        using var response = await JsonDocument.ParseAsync(context.Response.Body);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.StartsWith(ApiProblemDetails.ContentType, context.Response.ContentType);
        Assert.Equal(400, response.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Yêu cầu không hợp lệ", response.RootElement.GetProperty("title").GetString());
        Assert.Equal("https://httpstatuses.com/400", response.RootElement.GetProperty("type").GetString());
        Assert.Equal("Invalid operation.", response.RootElement.GetProperty("detail").GetString());
        Assert.Equal("/api/test", response.RootElement.GetProperty("instance").GetString());
        Assert.Equal("Invalid operation.", response.RootElement.GetProperty("message").GetString());
        Assert.Equal("stable_error", response.RootElement.GetProperty("code").GetString());
        Assert.Equal("request-trace-001", response.RootElement.GetProperty("traceId").GetString());
        Assert.Equal(string.Empty, response.RootElement.GetProperty("details").GetString());
        Assert.Equal(JsonValueKind.Null, response.RootElement.GetProperty("errors").ValueKind);
    }

    [Fact]
    public async Task ModelValidation_UsesProblemDetailsWithFieldErrors()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddECommerceControllers();
        using var provider = services.BuildServiceProvider();
        var apiBehavior = provider.GetRequiredService<IOptions<ApiBehaviorOptions>>().Value;
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "validation-trace-001"
        };
        httpContext.Request.Path = "/api/auth/register";
        httpContext.Response.Body = new MemoryStream();
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Email", "Email is required.");
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor(),
            modelState);

        var result = Assert.IsType<ApiProblemDetailsResult>(
            apiBehavior.InvalidModelStateResponseFactory(actionContext));
        var problem = result.Problem;

        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal("Dữ liệu gửi lên không hợp lệ.", problem.Detail);
        Assert.Equal("validation_error", problem.Extensions["code"]);
        Assert.Equal("validation-trace-001", problem.Extensions["traceId"]);
        var errors = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string[]>>(
            problem.Extensions["errors"]);
        Assert.Equal("Giá trị không hợp lệ hoặc không đúng định dạng.", Assert.Single(errors["Email"]));
        await result.ExecuteResultAsync(actionContext);
        Assert.StartsWith(ApiProblemDetails.ContentType, httpContext.Response.ContentType);
    }

    private static ExceptionMiddleware CreateExceptionMiddleware(RequestDelegate next)
        => new(
            next,
            new TestWebHostEnvironment(Path.GetTempPath()),
            NullLogger<ExceptionMiddleware>.Instance);
}
