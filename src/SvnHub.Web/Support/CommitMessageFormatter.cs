namespace SvnHub.Web.Support;

public static class CommitMessageFormatter
{
    public static string? FirstLine(string? log)
    {
        if (string.IsNullOrWhiteSpace(log))
        {
            return null;
        }

        var firstLine = log
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return null;
        }

        return firstLine;
    }

    public static string? FirstLine(string? log, int maxLength)
    {
        var firstLine = FirstLine(log);
        if (firstLine is null)
        {
            return null;
        }

        return firstLine.Length <= maxLength
            ? firstLine
            : firstLine[..maxLength] + "...";
    }
}
