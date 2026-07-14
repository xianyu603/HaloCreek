using System;
using System.Collections.Generic;
using System.Text.Json;
using HaloCreek.Infrastructure;

namespace HaloCreek.Services.CodexSessions
{
    internal sealed class CodexSessionJsonLine : IDisposable
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNameCaseInsensitive = false,
        };

        private readonly JsonDocument _document;

        public CodexSessionJsonLine(JsonDocument document)
        {
            _document = document;
        }

        public bool TryRead<TLine>(
            CodexSessionLineSchema<TLine> schema,
            out TLine line)
            where TLine : class
        {
            line = default!;

            if (!MatchesSchemaDiscriminator(schema))
            {
                return false;
            }

            var parsedLine = _document.RootElement.Deserialize<TLine>(SerializerOptions)
                ?? throw new JsonException("Codex session line is empty.");

            if (schema.IsMatch is not null && !schema.IsMatch(parsedLine))
            {
                return false;
            }

            schema.Validate?.Invoke(parsedLine);
            line = parsedLine;
            return true;
        }

        public void Dispose()
        {
            _document.Dispose();
        }

        private bool MatchesSchemaDiscriminator<TLine>(CodexSessionLineSchema<TLine> schema)
            where TLine : class
        {
            if (schema.RecordType is not null
                && !HasStringProperty(_document.RootElement, "type", schema.RecordType))
            {
                return false;
            }

            if (schema.PayloadType is null)
            {
                return true;
            }

            return _document.RootElement.TryGetProperty("payload", out var payload)
                && payload.ValueKind == JsonValueKind.Object
                && HasStringProperty(payload, "type", schema.PayloadType);
        }

        private static bool HasStringProperty(
            JsonElement element,
            string propertyName,
            string expectedValue)
        {
            return element.TryGetProperty(propertyName, out var property)
                && property.ValueKind == JsonValueKind.String
                && string.Equals(property.GetString(), expectedValue, StringComparison.Ordinal);
        }
    }

    internal static class CodexSessionJsonlReader
    {
        public static IEnumerable<CodexSessionJsonLine> ReadLines(string sessionFilePath)
        {
            foreach (var line in PlatformInfrastructure.ReadTextFileLinesWithWriteSharing(sessionFilePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var parsedLine = new CodexSessionJsonLine(JsonDocument.Parse(line));
                try
                {
                    yield return parsedLine;
                }
                finally
                {
                    parsedLine.Dispose();
                }
            }
        }
    }
}
