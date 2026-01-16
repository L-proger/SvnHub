using System.Text;

namespace SvnHub.Web.Support;

public static class SimpleSyntaxHighlighter
{
    private static readonly HashSet<string> CKeywords = new(StringComparer.Ordinal)
    {
        "auto","break","case","char","const","continue","default","do","double","else","enum","extern",
        "float","for","goto","if","inline","int","long","register","restrict","return","short","signed",
        "sizeof","static","struct","switch","typedef","union","unsigned","void","volatile","while",
        "_Alignas","_Alignof","_Atomic","_Bool","_Complex","_Generic","_Imaginary","_Noreturn","_Static_assert","_Thread_local"
    };

    private static readonly HashSet<string> CppKeywords = new(StringComparer.Ordinal)
    {
        "alignas","alignof","and","and_eq","asm","atomic_cancel","atomic_commit","atomic_noexcept","auto",
        "bitand","bitor","bool","break","case","catch","char","char8_t","char16_t","char32_t","class",
        "compl","concept","const","consteval","constexpr","constinit","const_cast","continue","co_await",
        "co_return","co_yield","decltype","default","delete","do","double","dynamic_cast","else","enum",
        "explicit","export","extern","false","float","for","friend","goto","if","inline","int","long",
        "mutable","namespace","new","noexcept","not","not_eq","nullptr","operator","or","or_eq","private",
        "protected","public","register","reinterpret_cast","requires","return","short","signed","sizeof",
        "static","static_assert","static_cast","struct","switch","template","this","thread_local","throw",
        "true","try","typedef","typeid","typename","union","unsigned","using","virtual","void","volatile",
        "wchar_t","while","xor","xor_eq"
    };

    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract","as","base","bool","break","byte","case","catch","char","checked","class","const",
        "continue","decimal","default","delegate","do","double","else","enum","event","explicit","extern",
        "false","finally","fixed","float","for","foreach","goto","if","implicit","in","int","interface",
        "internal","is","lock","long","namespace","new","null","object","operator","out","override","params",
        "private","protected","public","readonly","ref","return","sbyte","sealed","short","sizeof","stackalloc",
        "static","string","struct","switch","this","throw","true","try","typeof","uint","ulong","unchecked",
        "unsafe","ushort","using","virtual","void","volatile","while","record","init","var","dynamic","nint","nuint",
        "required","file"
    };

    private static readonly HashSet<string> VerilogKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "module","endmodule","begin","end","always","always_ff","always_comb","always_latch","assign","wire","reg",
        "logic","input","output","inout","parameter","localparam","genvar","generate","endgenerate","if","else",
        "case","endcase","for","foreach","while","repeat","forever","initial","function","endfunction","task","endtask",
        "typedef","struct","union","enum","packed","signed","unsigned","posedge","negedge"
    };

    public static string Highlight(string text, string language)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var keywords = language switch
        {
            "c" => CKeywords,
            "cpp" => CppKeywords,
            "csharp" => CSharpKeywords,
            "verilog" => VerilogKeywords,
            _ => null,
        };

        if (keywords is null)
        {
            return HtmlEncodeToString(text);
        }

        return HighlightLikeCLanguage(text, keywords, enableDirectives: language is "c" or "cpp" or "csharp");
    }

    private static string HighlightLikeCLanguage(string text, HashSet<string> keywords, bool enableDirectives)
    {
        var sb = new StringBuilder(text.Length + Math.Min(32_000, text.Length / 2));

        var i = 0;
        var atLineStart = true;

        while (i < text.Length)
        {
            var ch = text[i];

            if (ch == '\r')
            {
                AppendRaw(sb, "\r");
                i++;
                continue;
            }

            if (ch == '\n')
            {
                AppendRaw(sb, "\n");
                i++;
                atLineStart = true;
                continue;
            }

            if (enableDirectives && atLineStart && ch == '#')
            {
                var start = i;
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }

                AppendSpan(sb, "sh-dir", text.AsSpan(start, i - start));
                atLineStart = false;
                continue;
            }

            atLineStart = atLineStart && char.IsWhiteSpace(ch);

            if (ch == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                var start = i;
                i += 2;
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }

                AppendSpan(sb, "sh-com", text.AsSpan(start, i - start));
                continue;
            }

            if (ch == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var start = i;
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/'))
                {
                    i++;
                }

                if (i + 1 < text.Length)
                {
                    i += 2;
                }

                AppendSpan(sb, "sh-com", text.AsSpan(start, i - start));
                continue;
            }

            if (ch == '"')
            {
                var start = i;
                i++;
                while (i < text.Length)
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        i += 2;
                        continue;
                    }

                    if (text[i] == '"')
                    {
                        i++;
                        break;
                    }

                    if (text[i] == '\n')
                    {
                        break;
                    }

                    i++;
                }

                AppendSpan(sb, "sh-str", text.AsSpan(start, i - start));
                continue;
            }

            if (ch == '\'')
            {
                var start = i;
                i++;
                while (i < text.Length)
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        i += 2;
                        continue;
                    }

                    if (text[i] == '\'')
                    {
                        i++;
                        break;
                    }

                    if (text[i] == '\n')
                    {
                        break;
                    }

                    i++;
                }

                AppendSpan(sb, "sh-str", text.AsSpan(start, i - start));
                continue;
            }

            if (char.IsDigit(ch))
            {
                var start = i;
                i++;
                while (i < text.Length)
                {
                    var c = text[i];
                    if (char.IsLetterOrDigit(c) || c is '.' or '_' or 'x' or 'X')
                    {
                        i++;
                        continue;
                    }
                    break;
                }

                AppendSpan(sb, "sh-num", text.AsSpan(start, i - start));
                continue;
            }

            if (IsIdentStart(ch))
            {
                var start = i;
                i++;
                while (i < text.Length && IsIdentPart(text[i]))
                {
                    i++;
                }

                var word = text.AsSpan(start, i - start);
                if (keywords.Contains(word.ToString()))
                {
                    AppendSpan(sb, "sh-kw", word);
                }
                else
                {
                    AppendRaw(sb, word);
                }

                continue;
            }

            AppendRaw(sb, text.AsSpan(i, 1));
            i++;
        }

        return sb.ToString();
    }

    private static bool IsIdentStart(char ch) => char.IsLetter(ch) || ch == '_';
    private static bool IsIdentPart(char ch) => char.IsLetterOrDigit(ch) || ch == '_';

    private static void AppendSpan(StringBuilder sb, string cssClass, ReadOnlySpan<char> text)
    {
        sb.Append("<span class=\"");
        sb.Append(cssClass);
        sb.Append("\">");
        AppendHtmlEncoded(sb, text);
        sb.Append("</span>");
    }

    private static void AppendRaw(StringBuilder sb, ReadOnlySpan<char> text)
    {
        AppendHtmlEncoded(sb, text);
    }

    private static string HtmlEncodeToString(string text)
    {
        var sb = new StringBuilder(text.Length + Math.Min(16_000, text.Length / 4));
        AppendHtmlEncoded(sb, text.AsSpan());
        return sb.ToString();
    }

    private static void AppendHtmlEncoded(StringBuilder sb, ReadOnlySpan<char> text)
    {
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '&':
                    sb.Append("&amp;");
                    break;
                case '<':
                    sb.Append("&lt;");
                    break;
                case '>':
                    sb.Append("&gt;");
                    break;
                case '"':
                    sb.Append("&quot;");
                    break;
                case '\'':
                    sb.Append("&#39;");
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }
    }
}
