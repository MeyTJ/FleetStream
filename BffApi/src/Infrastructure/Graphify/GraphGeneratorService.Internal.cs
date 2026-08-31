using System.Text.RegularExpressions;

namespace FleetStream.Infrastructure.Graphify;

public sealed partial class GraphGeneratorService
{
    private static string SanitizeFileName(string name)
    {
        var invalid = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
        return Regex.Replace(name, $"[{invalid}]", "_");
    }

    private static IReadOnlyList<string> NormalizeExportFormats(IReadOnlyList<string> formats)
    {
        if (formats is not { Count: > 0 })
            return ["json", "html", "report"];

        return formats
            .Select(f => f.Trim().ToLowerInvariant())
            .Where(f => f.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
