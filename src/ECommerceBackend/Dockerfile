FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS restore
WORKDIR /source

COPY ["global.json", "."]
COPY ["Directory.Build.props", "."]
COPY ["src/ECommerceBackend.Domain/ECommerceBackend.Domain.csproj", "src/ECommerceBackend.Domain/"]
COPY ["src/ECommerceBackend.Application/ECommerceBackend.Application.csproj", "src/ECommerceBackend.Application/"]
COPY ["src/ECommerceBackend.Infrastructure/ECommerceBackend.Infrastructure.csproj", "src/ECommerceBackend.Infrastructure/"]
COPY ["src/ECommerceBackend/ECommerceBackend.csproj", "src/ECommerceBackend/"]
RUN dotnet restore "src/ECommerceBackend/ECommerceBackend.csproj"

FROM restore AS build
COPY src/ src/
RUN dotnet build "src/ECommerceBackend/ECommerceBackend.csproj" \
    --configuration Release \
    --no-restore

FROM build AS publish
RUN dotnet publish "src/ECommerceBackend/ECommerceBackend.csproj" \
    --configuration Release \
    --no-build \
    --output /app/publish \
    /p:UseAppHost=false

FROM build AS migrations
RUN dotnet tool install dotnet-ef \
    --tool-path /tools \
    --version 8.0.20
ENTRYPOINT ["/tools/dotnet-ef", "database", "update", "--project", "src/ECommerceBackend.Infrastructure/ECommerceBackend.Infrastructure.csproj", "--startup-project", "src/ECommerceBackend.Infrastructure/ECommerceBackend.Infrastructure.csproj", "--configuration", "Release", "--no-build", "--no-color"]

FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080

RUN mkdir -p /app/logs /app/Uploads/products /app/DataProtectionKeys \
    && chown -R app:app /app
COPY --from=publish --chown=app:app /app/publish .

USER app
ENTRYPOINT ["dotnet", "ECommerceBackend.dll"]
