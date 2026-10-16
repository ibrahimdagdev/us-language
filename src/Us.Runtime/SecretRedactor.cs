using System.Text;

namespace Us.Runtime;

public static class SecretRedactor
{
    public const string Mask = "***";

    public static bool IsSensitiveName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        return ContainsInsensitive(name, "parola")
            || ContainsInsensitive(name, "secret")
            || ContainsInsensitive(name, "token")
            || string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase);
    }

    public static string RedactNamed(string? name, string? value) =>
        IsSensitiveName(name) ? Mask : value ?? string.Empty;

    public static IReadOnlyDictionary<string, string> HeadersForLog(IReadOnlyDictionary<string, string> headers)
    {
        var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in headers)
        {
            copy[pair.Key] = RedactNamed(pair.Key, pair.Value);
        }

        return copy;
    }

    public static string FormatHeadersForLog(IReadOnlyDictionary<string, string> headers)
    {
        var builder = new StringBuilder();
        foreach (var pair in HeadersForLog(headers).OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (builder.Length > 0)
            {
                builder.Append("; ");
            }

            builder.Append(pair.Key).Append('=').Append(pair.Value);
        }

        return builder.ToString();
    }

    private static bool ContainsInsensitive(string text, string fragment) =>
        text.Contains(fragment, StringComparison.OrdinalIgnoreCase);
}
