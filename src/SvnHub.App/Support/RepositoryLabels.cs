namespace SvnHub.App.Support;

public static class RepositoryLabels
{
    public const int MaxLabels = 20;
    public const int MaxLabelLength = 40;

    public static IReadOnlyList<string> Normalize(IEnumerable<string>? labels)
    {
        if (labels is null)
        {
            return [];
        }

        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var label in labels)
        {
            var value = NormalizeOne(label);
            if (value is null || !seen.Add(value))
            {
                continue;
            }

            normalized.Add(value);
            if (normalized.Count >= MaxLabels)
            {
                break;
            }
        }

        return normalized;
    }

    public static bool TryParse(string? input, out IReadOnlyList<string> labels, out string? error)
    {
        labels = [];
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            return true;
        }

        var parsed = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in input.Split([',', ';', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var label = NormalizeOne(raw);
            if (label is null)
            {
                continue;
            }

            if (!IsValid(label, out error))
            {
                labels = [];
                return false;
            }

            if (seen.Add(label))
            {
                parsed.Add(label);
            }
        }

        if (parsed.Count > MaxLabels)
        {
            error = $"Use at most {MaxLabels} labels.";
            return false;
        }

        labels = parsed;
        return true;
    }

    public static IReadOnlyList<string> Collect(IEnumerable<SvnHub.Domain.Repository> repositories) =>
        repositories
            .SelectMany(r => Normalize(r.Labels))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static bool Contains(IEnumerable<string>? labels, string? label) =>
        !string.IsNullOrWhiteSpace(label) &&
        Normalize(labels).Contains(label.Trim(), StringComparer.OrdinalIgnoreCase);

    private static string? NormalizeOne(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value
            .Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        return parts.Length == 0 ? null : string.Join(" ", parts);
    }

    private static bool IsValid(string label, out string? error)
    {
        error = null;

        if (label.Length > MaxLabelLength)
        {
            error = $"Label '{label}' is too long. Use at most {MaxLabelLength} characters.";
            return false;
        }

        if (label.IndexOfAny(['/', '\\', ',', ';']) >= 0 || label.Any(char.IsControl))
        {
            error = $"Label '{label}' contains unsupported characters.";
            return false;
        }

        return true;
    }
}
