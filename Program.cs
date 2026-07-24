using System.Diagnostics;
using ECommerceBackend.API.Extensions;
using ECommerceBackend.API.Middlewares;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;

const string logOutputTemplate =
    "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] [{CorrelationId}] [{TraceId}/{SpanId}] {Message:lj}{NewLine}{Exception}";

Activity.DefaultIdFormat = ActivityIdFormat.W3C;
Activity.ForceDefaultIdFormat = true;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: logOutputTemplate)
    .WriteTo.File(
        "logs/log-.txt",
        rollingInterval: RollingInterval.Day,
        outputTemplate: logOutputTemplate)
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Configuration.AddECommerceLocalSettings(builder.Environment);
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(outputTemplate: logOutputTemplate)
        .WriteTo.File(
            "logs/log-.txt",
            rollingInterval: RollingInterval.Day,
            outputTemplate: logOutputTemplate));

    builder.Services
        .AddECommerceDataProtection(builder.Configuration, builder.Environment)
        .AddECommerceControllers()
        .AddECommerceConfigurationValidation(builder.Configuration, builder.Environment)
        .AddECommerceReverseProxy(builder.Configuration)
        .AddECommerceValidation()
        .AddECommerceMapping(builder.Configuration)
        .AddECommerceDatabase(builder.Configuration)
        .AddECommerceRepositoriesAndServices()
        .AddECommerceCors(builder.Configuration, builder.Environment)
        .AddECommerceJwtAuthentication(builder.Configuration, builder.Environment)
        .AddECommerceAuthorization()
        .AddECommerceRateLimiting()
        .AddECommerceHealthChecks();

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddECommerceSwagger();
    builder.ConfigureECommerceUploadLimits();

    var app = builder.Build();

    if (app.Configuration.GetValue<bool>($"{ECommerceBackend.Application.Common.ReverseProxyOptions.SectionName}:Enabled"))
        app.UseForwardedHeaders();

    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
        app.UseHttpsRedirection();
    }

    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseMiddleware<SecurityHeadersMiddleware>();

    app.UseProductImageStaticFiles(builder.Environment.ContentRootPath);

    app.UseCors();
    app.UseMiddleware<ExceptionMiddleware>();
    var swaggerEnabled = builder.Configuration.GetValue<bool?>("Swagger:Enabled")
        ?? app.Environment.IsDevelopment();
    if (swaggerEnabled)
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "ECommerce API v1");
            options.RoutePrefix = "swagger";
        });
    }

    app.UseAuthentication();
    app.UseRateLimiter();
    app.UseAuthorization();
    app.MapECommerceHealthChecks();
    app.MapControllers();

    app.Run();
}
catch (Exception ex) when (ex.GetType().Name == "HostAbortedException")
{
    // EF Core tooling aborts the host during design-time discovery.
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly.");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;
