using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace ECommerceBackend.API.Extensions
{
    public static class ProductImageStaticFilesExtensions
    {
        public const string RequestPath = "/uploads/products";

        public static IApplicationBuilder UseProductImageStaticFiles(
            this IApplicationBuilder app,
            string contentRootPath)
        {
            var imagesPath = Path.Combine(contentRootPath, "Uploads", "products");
            Directory.CreateDirectory(imagesPath);

            app.UseStaticFiles(CreateOptions(imagesPath));
            return app;
        }

        public static StaticFileOptions CreateOptions(string imagesPath)
        {
            var contentTypes = new FileExtensionContentTypeProvider();
            contentTypes.Mappings.Clear();
            contentTypes.Mappings[".jpg"] = "image/jpeg";
            contentTypes.Mappings[".jpeg"] = "image/jpeg";
            contentTypes.Mappings[".png"] = "image/png";
            contentTypes.Mappings[".webp"] = "image/webp";

            return new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(imagesPath),
                RequestPath = RequestPath,
                ContentTypeProvider = contentTypes,
                OnPrepareResponse = context =>
                {
                    context.Context.Response.Headers.XContentTypeOptions = "nosniff";
                    context.Context.Response.Headers.CacheControl = "public,max-age=86400";
                }
            };
        }
    }
}
