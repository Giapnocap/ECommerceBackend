namespace ECommerceBackend.API.Extensions
{
    public static class ConfigurationManagerExtensions
    {
        public static ConfigurationManager AddECommerceLocalSettings(
            this ConfigurationManager configuration,
            IWebHostEnvironment environment)
        {
            if (environment.IsDevelopment())
            {
                configuration.AddJsonFile(
                    "appsettings.Local.json",
                    optional: true,
                    reloadOnChange: true);
            }

            return configuration;
        }
    }
}
