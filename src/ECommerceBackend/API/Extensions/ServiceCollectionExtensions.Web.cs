using System.Reflection;
using Asp.Versioning;
using ECommerceBackend.API.Errors;
using ECommerceBackend.API.Swagger;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Validation;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.OpenApi.Models;

namespace ECommerceBackend.API.Extensions
{
    public static partial class ServiceCollectionExtensions
    {
        public static IServiceCollection AddECommerceControllers(this IServiceCollection services)
        {
            services.AddControllers();
            services.Replace(ServiceDescriptor.Singleton<
                ProblemDetailsFactory,
                ECommerceProblemDetailsFactory>());
            services.Configure<ApiBehaviorOptions>(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                {
                    var errors = context.ModelState
                        .Where(e => e.Value?.Errors.Count > 0)
                        .ToDictionary(
                            e => e.Key,
                            e => e.Value!.Errors.Select(err =>
                                GetModelErrorMessage(err.ErrorMessage)).ToArray());

                    return new ApiProblemDetailsResult(ApiProblemDetails.Create(
                        context.HttpContext,
                        StatusCodes.Status400BadRequest,
                        "validation_error",
                        "Dữ liệu gửi lên không hợp lệ.",
                        errors));
                };
            });

            return services;
        }

        public static IServiceCollection AddECommerceApiVersioning(this IServiceCollection services)
        {
            services
                .AddApiVersioning(options =>
                {
                    options.DefaultApiVersion = new ApiVersion(1, 0);
                    options.AssumeDefaultVersionWhenUnspecified = true;
                    options.ReportApiVersions = true;
                    options.ApiVersionReader = new UrlSegmentApiVersionReader();
                })
                .AddMvc()
                .AddApiExplorer(options =>
                {
                    options.GroupNameFormat = "'v'VVV";
                    options.SubstituteApiVersionInUrl = true;
                });

            return services;
        }

        public static IServiceCollection AddECommerceValidation(this IServiceCollection services)
        {
            services.AddFluentValidationAutoValidation();
            services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();
            return services;
        }

        public static WebApplicationBuilder ConfigureECommerceUploadLimits(this WebApplicationBuilder builder)
        {
            var uploadSection = builder.Configuration.GetSection(UploadOptions.SectionName);
            var maxImageSizeBytes = uploadSection.GetValue<long?>(nameof(UploadOptions.MaxImageSizeBytes))
                ?? UploadOptions.DefaultMaxImageSizeBytes;

            if (maxImageSizeBytes <= 0)
                throw new InvalidOperationException("Uploads:MaxImageSizeBytes must be greater than zero.");

            builder.Services.AddOptions<UploadOptions>()
                .Bind(uploadSection)
                .Validate(options => options.MaxImageSizeBytes > 0
                    && options.ReconciliationGraceMinutes is >= 1 and <= 10080
                    && options.MaxReconciliationDeletes is >= 1 and <= 1000,
                    "Upload limits or reconciliation config is invalid.")
                .ValidateOnStart();

            var maxRequestBodySize = checked(maxImageSizeBytes + 1024 * 1024);
            builder.Services.Configure<FormOptions>(options =>
            {
                options.MultipartBodyLengthLimit = maxRequestBodySize;
            });

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Limits.MaxRequestBodySize = maxRequestBodySize;
            });

            return builder;
        }

        public static IServiceCollection AddECommerceCors(
            this IServiceCollection services,
            IConfiguration configuration,
            IWebHostEnvironment environment)
        {
            var allowedOrigins = ResolveCorsOrigins(configuration, environment);

            services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.WithOrigins(allowedOrigins)
                          .AllowAnyHeader()
                          .AllowAnyMethod();
                });
            });

            return services;
        }

        public static IServiceCollection AddECommerceSwagger(this IServiceCollection services)
        {
            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "ECommerce API",
                    Version = "v1",
                    Description = "API E-Commerce phiên bản 1. Route /api/v1 được khuyến nghị; route /api được giữ để tương thích ngược."
                });

                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.Http,
                    Scheme = "Bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "Nhập token dạng: Bearer {token}"
                });

                options.DocInclusionPredicate(
                    (documentName, description) =>
                        description.GroupName == null
                        || string.Equals(
                            description.GroupName,
                            documentName,
                            StringComparison.OrdinalIgnoreCase));
                options.DocumentFilter<CanonicalApiVersionDocumentFilter>();
                options.OperationFilter<AuthorizeOperationFilter>();
                options.OperationFilter<RequestContractOperationFilter>();
                options.OperationFilter<DefaultResponseOperationFilter>();

                var documentedAssemblies = new[]
                {
                    Assembly.GetExecutingAssembly(),
                    typeof(RegisterRequestValidator).Assembly
                };
                foreach (var assembly in documentedAssemblies.Distinct())
                {
                    var xmlFile = $"{assembly.GetName().Name}.xml";
                    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                    if (File.Exists(xmlPath))
                        options.IncludeXmlComments(xmlPath);
                }
            });

            return services;
        }

        private static string GetModelErrorMessage(string? message)
        {
            if (string.IsNullOrWhiteSpace(message)
                || message.All(character => character <= sbyte.MaxValue))
            {
                return "Giá trị không hợp lệ hoặc không đúng định dạng.";
            }

            return message;
        }
    }
}
