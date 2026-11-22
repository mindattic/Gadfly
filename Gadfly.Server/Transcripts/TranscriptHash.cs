using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gadfly.Server.Transcripts;

/// <summary>
/// The transcript's content hash and verification code (GAD-T295).
/// <para>Canonical JSON: the transcript object with every object's keys in ordinal order, null members dropped, no
/// whitespace, numbers exactly as written and strings escaped the same way every time. The hash is the SHA-256 of that
/// text in UTF-8. Because the form is canonical, a JSON file that was only re-indented or had its keys reordered still
/// verifies; a changed word does not.</para>
/// <para>The verification code is the first 80 bits of the hash in Crockford base 32 (no I, L, O or U), in four groups of
/// four: short enough to read out or type from paper, long enough that it cannot be guessed.</para>
/// </summary>
public static class TranscriptHash
{
    /// <summary>How a transcript is written as JSON: camelCase, enums by name, nulls left out.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    public const int CodeLength = 16;

    public static TranscriptStamp Stamp(TranscriptDocument document, DateTimeOffset generatedAt, string verifyBaseUrl)
    {
        var sha = Sha256(Canonical(document));
        var code = Code(sha);
        return new TranscriptStamp(sha, code, generatedAt, VerifyUrl(verifyBaseUrl, code));
    }

    public static string VerifyUrl(string baseUrl, string code) => baseUrl.TrimEnd('/') + "/transcripts/verify?code=" + code;

    public static string Canonical(TranscriptDocument document) => Canonical(JsonSerializer.SerializeToElement(document, Json));

    /// <summary>The canonical form of any JSON value (see the class remarks).</summary>
    public static string Canonical(JsonElement element)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) Write(writer, element);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void Write(Utf8JsonWriter w, JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                w.WriteStartObject();
                foreach (var p in e.EnumerateObject().Where(p => p.Value.ValueKind != JsonValueKind.Null).OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    w.WritePropertyName(p.Name);
                    Write(w, p.Value);
                }
                w.WriteEndObject();
                break;
            case JsonValueKind.Array:
                w.WriteStartArray();
                foreach (var item in e.EnumerateArray()) Write(w, item);
                w.WriteEndArray();
                break;
            case JsonValueKind.String: w.WriteStringValue(e.GetString()); break;
            case JsonValueKind.Number: w.WriteRawValue(e.GetRawText(), skipInputValidation: true); break;
            case JsonValueKind.True: w.WriteBooleanValue(true); break;
            case JsonValueKind.False: w.WriteBooleanValue(false); break;
            default: w.WriteNullValue(); break;
        }
    }

    /// <summary>Lower-case hex SHA-256 of <paramref name="canonical"/> in UTF-8.</summary>
    public static string Sha256(string canonical) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

    /// <summary>The verification code of a hash: its first 80 bits, e.g. "7K3M-9QXA-B2CD-EFGH".</summary>
    public static string Code(string sha256Hex)
    {
        var bytes = Convert.FromHexString(sha256Hex.AsSpan(0, 20));   // 10 bytes = 80 bits = 16 characters of 5 bits
        var chars = new char[CodeLength];
        for (var i = 0; i < CodeLength; i++)
        {
            var bit = i * 5;
            var value = ((bytes[bit / 8] << 8) | (bit / 8 + 1 < bytes.Length ? bytes[bit / 8 + 1] : 0)) >> (11 - bit % 8) & 31;
            chars[i] = Alphabet[value];
        }
        return Group(new string(chars));
    }

    /// <summary>
    /// A code as a person typed it, in its printed form: case, spaces and dashes are ignored, and the letters people
    /// mistake for digits (O for 0, I and L for 1) are read as the digit. Null when it is not a code.
    /// </summary>
    public static string? NormalizeCode(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed) || typed.Length > 64) return null;
        var sb = new StringBuilder(CodeLength);
        foreach (var raw in typed.Trim().ToUpperInvariant())
        {
            if (raw is '-' or ' ' or '–' or '—' or '.') continue;
            var c = raw switch { 'O' => '0', 'I' or 'L' => '1', _ => raw };
            if (!Alphabet.Contains(c)) return null;
            sb.Append(c);
        }
        return sb.Length == CodeLength ? Group(sb.ToString()) : null;
    }

    /// <summary>True for 64 hex characters.</summary>
    public static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static string Group(string sixteen) => string.Join('-', Enumerable.Range(0, 4).Select(i => sixteen.Substring(i * 4, 4)));
}
