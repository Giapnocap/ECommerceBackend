using System.Text;
using System.Text.Json;

namespace ECommerceBackend.Tests;

public sealed class OpenApiBaselineTests : IClassFixture<TestApiFactory>
{
    private const string UpdateBaselineVariable = "UPDATE_OPENAPI_BASELINE";
    private readonly TestApiFactory _factory;

    public OpenApiBaselineTests(TestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task OpenApi_MatchesVersionedBaseline()
    {
        using var client = await _factory.CreateInitializedClientAsync();
        var currentDocument = await client.GetStringAsync("/swagger/v1/swagger.json");
        var canonicalDocument = Canonicalize(currentDocument);
        var baselinePath = GetBaselinePath();

        if (string.Equals(
            Environment.GetEnvironmentVariable(UpdateBaselineVariable),
            "1",
            StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
            await File.WriteAllTextAsync(
                baselinePath,
                canonicalDocument + Environment.NewLine,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return;
        }

        Assert.True(
            File.Exists(baselinePath),
            $"OpenAPI baseline was not found at '{baselinePath}'.");

        var expectedDocument = Canonicalize(await File.ReadAllTextAsync(baselinePath));
        Assert.Equal(expectedDocument, canonicalDocument);
    }

    private static string Canonicalize(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            WriteCanonical(writer, document.RootElement);

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element
                    .EnumerateObject()
                    .OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static string GetBaselinePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null
            && !File.Exists(Path.Combine(directory.FullName, "ECommerceBackend.sln")))
        {
            directory = directory.Parent;
        }

        if (directory == null)
            throw new InvalidOperationException("Could not locate the repository root.");

        return Path.Combine(directory.FullName, "docs", "contracts", "openapi-v1.json");
    }
}
