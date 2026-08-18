using System.Text.Json;

namespace ECommerceBackend.Application.Services
{
    public static class AuditMetadataRedactor
    {
        private const string RedactedValue = "[REDACTED]";

        public static string? Serialize(IReadOnlyDictionary<string, object?>? metadata)
            => metadata is { Count: > 0 }
                ? RedactJson(JsonSerializer.Serialize(metadata))
                : null;

        public static string? RedactJson(string? metadataJson)
        {
            if (string.IsNullOrWhiteSpace(metadataJson))
                return metadataJson;

            try
            {
                using var document = JsonDocument.Parse(metadataJson);
                return document.RootElement.ValueKind == JsonValueKind.Object
                    ? JsonSerializer.Serialize(RedactElement(document.RootElement))
                    : JsonSerializer.Serialize(new { redacted = true });
            }
            catch (JsonException)
            {
                return JsonSerializer.Serialize(new { redacted = true });
            }
        }

        private static IReadOnlyDictionary<string, object?> RedactElement(JsonElement element)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                result[property.Name] = IsSensitiveProperty(property.Name)
                    ? RedactedValue
                    : RedactValue(property.Value);
            }

            return result;
        }

        private static object? RedactValue(JsonElement value)
            => value.ValueKind switch
            {
                JsonValueKind.Object => RedactElement(value),
                JsonValueKind.Array => value
                    .EnumerateArray()
                    .Select(RedactValue)
                    .ToList(),
                JsonValueKind.String => value.GetString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => value.Clone()
            };

        private static bool IsSensitiveProperty(string name)
        {
            var normalized = name
                .Replace("_", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .ToLowerInvariant();
            return normalized is "password"
                or "passwordhash"
                or "currentpassword"
                or "newpassword"
                or "token"
                or "refreshtoken"
                or "accesstoken"
                or "tokenhash"
                or "replacedbytokenhash"
                or "authorization"
                or "secret"
                or "clientsecret"
                or "apikey"
                or "credential"
                or "credentials"
                or "signingkey"
                or "jwtsigningkey";
        }
    }
}
