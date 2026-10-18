using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Sapling.Shared.Content;

/// <summary>
/// Turns a Sapling AI reply into safe HTML. Models answer in light markdown (bold, lists, the odd heading),
/// which reads as stray asterisks and hashes when shown as plain text. Everything is HTML-encoded first, so
/// only the tags written here can reach the page.
/// </summary>
public static partial class ChatMarkup
{
    public static string ToHtml(string? text)
    {
        var lines = Normalise(text).Split('\n');
        var html = new StringBuilder();
        var paragraph = new List<string>();
        string? list = null;

        void ClosePara()
        {
            if (paragraph.Count > 0)
            {
                html.Append("<p>").Append(string.Join("<br>", paragraph)).Append("</p>");
                paragraph.Clear();
            }
        }

        void CloseList()
        {
            if (list is not null)
            {
                html.Append("</").Append(list).Append('>');
                list = null;
            }
        }

        void Item(string tag, string content)
        {
            ClosePara();
            if (list != tag)
            {
                CloseList();
                html.Append('<').Append(tag).Append('>');
                list = tag;
            }

            html.Append("<li>").Append(Inline(content)).Append("</li>");
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                ClosePara();
                CloseList();
                continue;
            }

            if (Bullet().Match(line) is { Success: true } bullet)
            {
                Item("ul", bullet.Groups[1].Value);
            }
            else if (Numbered().Match(line) is { Success: true } numbered)
            {
                Item("ol", numbered.Groups[1].Value);
            }
            else if (Heading().Match(line) is { Success: true } heading)
            {
                ClosePara();
                CloseList();
                html.Append("<p><strong>").Append(Inline(heading.Groups[1].Value.Trim('*', ' '))).Append("</strong></p>");
            }
            else if (line.StartsWith('|'))
            {
                // A table has no room in a chat bubble: each row becomes one line, and the rule row is dropped.
                if (!TableRule().IsMatch(line))
                {
                    CloseList();
                    var cells = line.Trim('|').Split('|').Select(c => c.Trim()).Where(c => c.Length > 0);
                    paragraph.Add(Inline(string.Join(" · ", cells)));
                }
            }
            else
            {
                CloseList();
                paragraph.Add(Inline(line));
            }
        }

        ClosePara();
        CloseList();
        return html.ToString();
    }

    /// <summary>Characters some models emit that fonts render badly, swapped for their plain forms.</summary>
    public static string Normalise(string? text) => (text ?? "")
        .Replace("\r\n", "\n")
        .Replace('‑', '-')  // non-breaking hyphen
        .Replace('‐', '-')  // hyphen
        .Replace(' ', ' ')  // narrow no-break space
        .Replace(' ', ' ')  // no-break space
        .Replace("​", "")   // zero-width space
        .Trim();

    private static string Inline(string text)
    {
        var s = WebUtility.HtmlEncode(text);
        s = Code().Replace(s, "<code>$1</code>");
        s = Bold().Replace(s, "<strong>$1</strong>");
        s = Italic().Replace(s, "$1<em>$2</em>");
        s = Link().Replace(s, "<a href=\"$2\" target=\"_blank\" rel=\"noopener noreferrer\">$1</a>");
        return s;
    }

    [GeneratedRegex(@"^(?:[-*•–]|•)\s+(.+)$")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^\d{1,2}[.)]\s+(.+)$")]
    private static partial Regex Numbered();

    [GeneratedRegex(@"^#{1,6}\s+(.+)$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^\|?[\s:|-]+\|?$")]
    private static partial Regex TableRule();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex Code();

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex Bold();

    [GeneratedRegex(@"(^|[\s(])\*([^*\s][^*]*?)\*(?=$|[\s).,;:!?])")]
    private static partial Regex Italic();

    // Runs after encoding, so the address can only contain what HtmlEncode left: no quotes or angle brackets.
    [GeneratedRegex(@"\[([^\]]+)\]\((https?://[^\s)]+)\)")]
    private static partial Regex Link();
}
