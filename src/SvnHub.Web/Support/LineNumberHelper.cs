using System.Text;

namespace SvnHub.Web.Support;

public static class LineNumberHelper
{
    public static int CountLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 1;
        }

        var lines = 1;
        foreach (var ch in text)
        {
            if (ch == '\n')
            {
                lines++;
            }
        }

        return lines;
    }

    public static string Build(string? text)
    {
        var lineCount = CountLines(text);
        var sb = new StringBuilder(lineCount * 4);
        for (var i = 1; i <= lineCount; i++)
        {
            sb.Append(i);
            if (i != lineCount)
            {
                sb.Append('\n');
            }
        }

        return sb.ToString();
    }
}

