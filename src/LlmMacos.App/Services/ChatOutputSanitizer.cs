using System.Text.RegularExpressions;

namespace LlmMacos.App.Services;

internal static partial class ChatOutputSanitizer
{
    private static readonly string[] TailMarkers = ["User:", "System:"];

    public static string Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var cleaned = text
            .Replace("\uFFFD", string.Empty, StringComparison.Ordinal)
            .Replace("\0", string.Empty, StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        foreach (var marker in TailMarkers)
        {
            cleaned = TrimTrailingMarker(cleaned, marker);
        }

        cleaned = ExcessiveBlankLinesRegex().Replace(cleaned, "\n\n");
        return cleaned.TrimEnd();
    }

    private static string TrimTrailingMarker(string input, string marker)
    {
        var markerStart = input.LastIndexOf(marker, StringComparison.Ordinal);
        if (markerStart < 0)
        {
            return input;
        }

        var afterMarkerStart = markerStart + marker.Length;
        if (afterMarkerStart >= input.Length)
        {
            return input[..markerStart].TrimEnd();
        }

        var tail = input[afterMarkerStart..];
        return string.IsNullOrWhiteSpace(tail) ? input[..markerStart].TrimEnd() : input;
    }

    [GeneratedRegex("\\n{3,}", RegexOptions.Compiled)]
    private static partial Regex ExcessiveBlankLinesRegex();
}
