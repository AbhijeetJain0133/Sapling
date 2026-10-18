using System.Text;
using System.Text.RegularExpressions;

namespace Sapling.Web.Services;

/// <summary>
/// Repairs a LaTeX document that came back from the AI "flattened": one long line with the two characters
/// <c>\n</c> where each line break should be. It happens when a model escapes line breaks twice, or leaves
/// backslashes unescaped so its JSON cannot be parsed and the raw text is used. TeX then reads <c>\n</c> as
/// a command and stops with "Undefined control sequence".
/// </summary>
public static partial class LatexText
{
    // Real commands that begin with \n. Anywhere else, \n followed by letters is a line break before text.
    private static readonly HashSet<string> CommandsStartingWithN = new(StringComparer.Ordinal)
    {
        "newcommand", "newenvironment", "newline", "newpage", "newlength", "newcounter", "newif", "newcolumntype",
        "newtheorem", "newfontfamily", "newsavebox", "noindent", "normalsize", "normalfont", "nobreak", "nolinebreak",
        "nopagebreak", "nobreakspace", "nolimits", "nonumber", "noalign", "nointerlineskip", "numberline",
    };

    // Left out on purpose: short maths and counter commands spelt like ordinary words ("not", "number",
    // "null"). A resume has no use for them, and a line of text starting with one of those words must not
    // be glued to the line above.

    public static string Unflatten(string latex)
    {
        // A real document has line breaks. One without any, but with \n sequences, is a flattened one.
        if (string.IsNullOrEmpty(latex) || latex.Contains('\n') || !latex.Contains("\\n", StringComparison.Ordinal))
        {
            return latex;
        }

        // An escaped Windows line ending is \r\n; the \r half carries nothing.
        var text = latex.Replace("\\r\\n", "\\n", StringComparison.Ordinal);

        text = EscapedNewline().Replace(text, match =>
        {
            var slashes = match.Groups[1].Value;
            var letters = match.Groups[2].Value;

            // An even run is LaTeX's own \\ line break followed by a plain "n". In an odd run the last
            // backslash belongs to the n: either a real command such as \newcommand, or an escaped line break.
            if (slashes.Length % 2 == 0 || CommandsStartingWithN.Contains("n" + letters))
            {
                return match.Value;
            }

            return slashes[..^1] + "\n" + letters;
        });

        // Left over from the same JSON escaping: \" for a quote, and \t for a tab where no command follows.
        text = EscapedQuote().Replace(text, match => OddRun(match) ? match.Groups[1].Value[..^1] + "\"" : match.Value);
        text = EscapedTab().Replace(text, match => OddRun(match) ? match.Groups[1].Value[..^1] + " " : match.Value);
        return text;
    }

    private static bool OddRun(Match match) => match.Groups[1].Value.Length % 2 == 1;

    // & separates columns inside these, so it is left alone there.
    private static readonly HashSet<string> TableEnvironments = new(StringComparer.Ordinal)
    {
        "tabular", "tabularx", "tabulary", "longtable", "array", "tabu", "matrix", "pmatrix", "bmatrix", "cases",
    };

    // Maths and verbatim text: nothing inside is touched.
    private static readonly HashSet<string> RawEnvironments = new(StringComparer.Ordinal)
    {
        "equation", "align", "aligned", "gather", "multline", "eqnarray", "math", "displaymath", "verbatim", "lstlisting",
    };

    // Commands whose first argument is a URL or a name, where _ # % and & are meant literally.
    private static readonly HashSet<string> LiteralArgumentCommands = new(StringComparer.Ordinal)
    {
        "href", "url", "hyperref", "label", "ref", "pageref", "includegraphics", "input", "include", "hypersetup",
    };

    private static readonly HashSet<string> DefinitionCommands = new(StringComparer.Ordinal)
    {
        "newcommand", "renewcommand", "providecommand", "def", "newenvironment", "renewenvironment",
    };

    /// <summary>
    /// Escapes the characters that stop a resume compiling when they are left bare in ordinary text: "&amp;" outside
    /// a table, "#", "_" and "^" outside maths, "%" straight after a digit (as in "25%") and "$" before a number.
    /// Models leave these in when they rewrite a line ("Computer Science &amp; Engineering"). Only the body is
    /// touched, and commands, URLs, comments, maths and tables are left alone, so a correct document comes back
    /// unchanged.
    /// </summary>
    public static string EscapeStrayCharacters(string latex)
    {
        const string begin = "\\begin{document}";
        var start = string.IsNullOrEmpty(latex) ? -1 : latex.IndexOf(begin, StringComparison.Ordinal);
        if (start < 0)
        {
            return latex;
        }

        var sb = new StringBuilder(latex.Length + 32);
        sb.Append(latex, 0, start + begin.Length);
        var body = MarkdownBold().Replace(latex[(start + begin.Length)..], @"\textbf{$1}");

        var tables = 0;
        var raw = 0;
        var maths = false;
        var i = 0;
        while (i < body.Length)
        {
            var c = body[i];
            if (c == '\\')
            {
                var end = i + 1;
                while (end < body.Length && char.IsAsciiLetter(body[end]))
                {
                    end++;
                }

                var name = body[(i + 1)..end];
                if (name.Length == 0)
                {
                    // \& \% \\ and friends are already escaped; \( \[ open maths and \) \] close it.
                    var next = i + 1 < body.Length ? body[i + 1] : '\0';
                    maths = next is '(' or '[' || (next is not (')' or ']') && maths);
                    sb.Append(body, i, Math.Min(2, body.Length - i));
                    i += 2;
                    continue;
                }

                if (name is "begin" or "end" && end < body.Length && body[end] == '{')
                {
                    var close = body.IndexOf('}', end);
                    if (close > 0)
                    {
                        var environment = body[(end + 1)..close].TrimEnd('*');
                        var step = name == "begin" ? 1 : -1;
                        if (TableEnvironments.Contains(environment))
                        {
                            tables = Math.Max(0, tables + step);
                        }
                        else if (RawEnvironments.Contains(environment))
                        {
                            raw = Math.Max(0, raw + step);
                        }

                        end = close + 1;
                    }
                }
                else if (LiteralArgumentCommands.Contains(name))
                {
                    end = EndOfGroup(body, end);
                }
                else if (DefinitionCommands.Contains(name))
                {
                    var lineEnd = body.IndexOf('\n', end);
                    end = lineEnd < 0 ? body.Length : lineEnd;
                }

                sb.Append(body, i, end - i);
                i = end;
                continue;
            }

            if (raw > 0)
            {
                sb.Append(c);
                i++;
                continue;
            }

            switch (c)
            {
                case '%' when i > 0 && char.IsAsciiDigit(body[i - 1]):
                    sb.Append(@"\%");
                    break;
                case '%':
                    // A comment runs to the end of the line and is copied as it is.
                    var stop = body.IndexOf('\n', i);
                    stop = stop < 0 ? body.Length : stop;
                    sb.Append(body, i, stop - i);
                    i = stop;
                    continue;
                case '$' when !maths && i + 1 < body.Length && char.IsAsciiDigit(body[i + 1]) && !ClosesOnLine(body, i + 1):
                    sb.Append(@"\$");
                    break;
                case '$':
                    maths = !maths;
                    sb.Append(c);
                    break;
                case '&' when tables == 0:
                    sb.Append(@"\&");
                    break;
                case '#':
                    sb.Append(@"\#");
                    break;
                case '_' when !maths:
                    sb.Append(@"\_");
                    break;
                case '^' when !maths:
                    sb.Append(@"\textasciicircum{}");
                    break;
                default:
                    sb.Append(c);
                    break;
            }

            i++;
        }

        return sb.ToString();
    }

    /// <summary>The index just past the {...} group that starts at or after <paramref name="at"/>, skipping an [option] first.</summary>
    private static int EndOfGroup(string text, int at)
    {
        var i = at;
        if (i < text.Length && text[i] == '[')
        {
            var close = text.IndexOf(']', i);
            i = close < 0 ? i : close + 1;
        }

        if (i >= text.Length || text[i] != '{')
        {
            return at;
        }

        var depth = 0;
        for (; i < text.Length; i++)
        {
            if (text[i] == '\\')
            {
                i++;
            }
            else if (text[i] == '{')
            {
                depth++;
            }
            else if (text[i] == '}' && --depth == 0)
            {
                return i + 1;
            }
        }

        return text.Length;
    }

    private static bool ClosesOnLine(string text, int from)
    {
        for (var i = from; i < text.Length && text[i] != '\n'; i++)
        {
            if (text[i] == '\\')
            {
                i++;
            }
            else if (text[i] == '$')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The lines of a compile log that say what went wrong, short enough to hand back to the model.</summary>
    public static string CompileErrors(string log)
    {
        var lines = (log ?? "").Replace("\r", "", StringComparison.Ordinal).Split('\n');
        var picked = new List<string>();
        for (var i = 0; i < lines.Length && picked.Count < 16; i++)
        {
            // TeX reports "! Message." and then the line it stopped on, split in two at the exact spot.
            if (lines[i].StartsWith("! ", StringComparison.Ordinal))
            {
                picked.AddRange(lines.Skip(i).Take(4));
                i += 3;
            }
        }

        if (picked.Count == 0)
        {
            picked.AddRange(lines.Where(l => l.StartsWith("error:", StringComparison.Ordinal)).Take(6));
        }

        return picked.Count == 0 ? "The document did not compile." : string.Join("\n", picked);
    }

    [GeneratedRegex(@"(\\+)n([A-Za-z]*)")]
    private static partial Regex EscapedNewline();

    [GeneratedRegex(@"(\\+)""")]
    private static partial Regex EscapedQuote();

    [GeneratedRegex(@"(\\+)t(?![A-Za-z])")]
    private static partial Regex EscapedTab();

    // Markdown bold, which models slip into resumes; TeX would print the asterisks.
    [GeneratedRegex(@"\*\*([^*\n]+)\*\*")]
    private static partial Regex MarkdownBold();
}
