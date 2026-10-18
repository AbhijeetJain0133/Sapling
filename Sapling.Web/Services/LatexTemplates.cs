using System.Text;
using Sapling.Shared.Contracts;

namespace Sapling.Web.Services;

/// <summary>
/// Renders structured resume content into one of the two ATS templates. Output always compiles: every value is
/// escaped here (DataJson keeps raw text), and the preamble works under XeTeX (Tectonic) and pdfLaTeX (Overleaf).
/// </summary>
public static class LatexTemplates
{
    public static string Render(string template, ResumeDataDto data) =>
        template == ResumeTemplates.TwoColumn ? TwoColumn(data) : SingleColumn(data);

    /// <summary>
    /// Kept small on purpose: every package is a download on the first compile. The AI editor is told to keep it
    /// and to reuse \entry and \subentry, so edited documents stay in the same shape.
    /// </summary>
    public const string Preamble = """
        \documentclass[10pt,a4paper]{article}
        \usepackage[margin=0.6in]{geometry}
        \usepackage{iftex}
        \ifPDFTeX
          \usepackage[T1]{fontenc}
          \usepackage[utf8]{inputenc}
          \usepackage{lmodern}
        \else
          \usepackage{fontspec}
        \fi
        \usepackage{enumitem}
        \usepackage{titlesec}
        \usepackage[hidelinks]{hyperref}
        \pagestyle{empty}
        \setlength{\parindent}{0pt}
        \setlist[itemize]{leftmargin=1.2em,nosep,itemsep=1pt,topsep=2pt}
        \titleformat{\section}{\large\bfseries\scshape}{}{0em}{}[\vspace{-7pt}\rule{\linewidth}{0.5pt}]
        \titlespacing*{\section}{0pt}{9pt}{4pt}
        % \entry{title}{dates} starts an item; \subentry{detail}{location} is its optional second line.
        \newcommand{\entry}[2]{\par\textbf{#1}\hfill #2\par}
        \newcommand{\subentry}[2]{\textit{#1}\hfill\textit{#2}\par}
        """;

    private static string SingleColumn(ResumeDataDto d)
    {
        var sb = new StringBuilder(Preamble).Append("\n\\begin{document}\n\n");
        sb.Append("\\begin{center}\n");
        sb.Append("  {\\LARGE\\bfseries ").Append(Esc(Name(d))).Append("}\\\\[4pt]\n");
        var contact = ContactParts(d.Contact);
        if (contact.Count > 0)
        {
            sb.Append("  ").Append(string.Join(" $\\cdot$ ", contact)).Append('\n');
        }

        sb.Append("\\end{center}\n");

        Summary(sb, d);
        Education(sb, d);
        Experience(sb, d);
        Projects(sb, d);
        Skills(sb, d);
        Achievements(sb, d);

        return sb.Append("\n\\end{document}\n").ToString();
    }

    private static string TwoColumn(ResumeDataDto d)
    {
        var sb = new StringBuilder(Preamble).Append("\n\\begin{document}\n\n");
        sb.Append("{\\LARGE\\bfseries ").Append(Esc(Name(d))).Append("}\\par\\vspace{6pt}\n\n");

        sb.Append("\\begin{minipage}[t]{0.31\\linewidth}\n");
        var contact = ContactParts(d.Contact);
        if (contact.Count > 0)
        {
            sb.Append("\\section{Contact}\n");
            foreach (var part in contact)
            {
                sb.Append(part).Append("\\par\n");
            }
        }

        Skills(sb, d);
        Education(sb, d);
        Achievements(sb, d);
        sb.Append("\\end{minipage}\\hfill\n");

        sb.Append("\\begin{minipage}[t]{0.65\\linewidth}\n");
        Summary(sb, d);
        Experience(sb, d);
        Projects(sb, d);
        sb.Append("\\end{minipage}\n");

        return sb.Append("\n\\end{document}\n").ToString();
    }

    private static string Name(ResumeDataDto d) =>
        string.IsNullOrWhiteSpace(d.Contact.FullName) ? "Your Name" : d.Contact.FullName;

    private static List<string> ContactParts(ResumeContactDto c)
    {
        var parts = new List<string>();
        if (Has(c.Email))
        {
            parts.Add($"\\href{{mailto:{EscUrl(c.Email, scheme: false)}}}{{{Esc(c.Email)}}}");
        }

        if (Has(c.Phone))
        {
            parts.Add(Esc(c.Phone));
        }

        if (Has(c.Location))
        {
            parts.Add(Esc(c.Location));
        }

        foreach (var link in new[] { c.LinkedIn, c.GitHub, c.Website }.Where(Has))
        {
            parts.Add(Link(link));
        }

        return parts;
    }

    private static void Summary(StringBuilder sb, ResumeDataDto d)
    {
        if (!Has(d.Summary))
        {
            return;
        }

        sb.Append("\n\\section{Summary}\n").Append(Esc(d.Summary)).Append("\\par\n");
    }

    private static void Education(StringBuilder sb, ResumeDataDto d)
    {
        if (d.Education.Count == 0)
        {
            return;
        }

        sb.Append("\n\\section{Education}\n");
        foreach (var e in d.Education)
        {
            sb.Append("\\entry{").Append(Esc(e.Institution)).Append("}{").Append(Esc(Dates(e.Start, e.End))).Append("}\n");
            var degree = string.Join(", ", new[] { e.Degree, e.Field }.Where(Has));
            SubEntry(sb, degree, e.Grade);
            Bullets(sb, e.Highlights);
            sb.Append("\\vspace{4pt}\n");
        }
    }

    private static void Experience(StringBuilder sb, ResumeDataDto d)
    {
        if (d.Experience.Count == 0)
        {
            return;
        }

        sb.Append("\n\\section{Experience}\n");
        foreach (var e in d.Experience)
        {
            sb.Append("\\entry{").Append(Esc(Has(e.Role) ? e.Role : e.Organisation)).Append("}{").Append(Esc(Dates(e.Start, e.End))).Append("}\n");
            SubEntry(sb, Has(e.Role) ? e.Organisation : "", e.Location);
            Bullets(sb, e.Bullets);
            sb.Append("\\vspace{4pt}\n");
        }
    }

    private static void Projects(StringBuilder sb, ResumeDataDto d)
    {
        if (d.Projects.Count == 0)
        {
            return;
        }

        sb.Append("\n\\section{Projects}\n");
        foreach (var p in d.Projects)
        {
            var title = Esc(p.Name);
            if (Has(p.Link))
            {
                title += $" \\normalfont\\small(\\href{{{EscUrl(p.Link)}}}{{{Esc(Display(p.Link))}}})";
            }

            sb.Append("\\entry{").Append(title).Append("}{").Append(Esc(Dates(p.Start, p.End))).Append("}\n");
            SubEntry(sb, p.Technologies, "");
            Bullets(sb, p.Bullets);
            sb.Append("\\vspace{4pt}\n");
        }
    }

    private static void Skills(StringBuilder sb, ResumeDataDto d)
    {
        if (d.Skills.Count == 0)
        {
            return;
        }

        sb.Append("\n\\section{Skills}\n");
        foreach (var g in d.Skills)
        {
            var items = string.Join(", ", g.Items.Where(Has).Select(Esc));
            sb.Append(Has(g.Category) ? $"\\textbf{{{Esc(g.Category)}:}} " : "").Append(items).Append("\\par\n");
        }
    }

    private static void Achievements(StringBuilder sb, ResumeDataDto d)
    {
        if (d.Achievements.Count == 0)
        {
            return;
        }

        sb.Append("\n\\section{Certifications and Achievements}\n");
        sb.Append("\\begin{itemize}\n");
        foreach (var a in d.Achievements)
        {
            sb.Append("  \\item \\textbf{").Append(Esc(a.Title)).Append('}');
            if (Has(a.Issuer))
            {
                sb.Append(", ").Append(Esc(a.Issuer));
            }

            if (Has(a.Date))
            {
                sb.Append(" (").Append(Esc(a.Date)).Append(')');
            }

            if (Has(a.Detail))
            {
                sb.Append(". ").Append(Esc(a.Detail));
            }

            sb.Append('\n');
        }

        sb.Append("\\end{itemize}\n");
    }

    private static void SubEntry(StringBuilder sb, string left, string right)
    {
        if (Has(left) || Has(right))
        {
            sb.Append("\\subentry{").Append(Esc(left)).Append("}{").Append(Esc(right)).Append("}\n");
        }
    }

    private static void Bullets(StringBuilder sb, IReadOnlyList<string> bullets)
    {
        var items = bullets.Where(Has).ToList();
        if (items.Count == 0)
        {
            return;
        }

        sb.Append("\\begin{itemize}\n");
        foreach (var b in items)
        {
            sb.Append("  \\item ").Append(Esc(b)).Append('\n');
        }

        sb.Append("\\end{itemize}\n");
    }

    private static string Dates(string start, string end) =>
        (Has(start), Has(end)) switch
        {
            (true, true) => $"{start.Trim()} – {end.Trim()}",
            (true, false) => start.Trim(),
            (false, true) => end.Trim(),
            _ => "",
        };

    private static string Link(string url) => $"\\href{{{EscUrl(url)}}}{{{Esc(Display(url))}}}";

    private static bool Has(string? s) => !string.IsNullOrWhiteSpace(s);

    private static string Display(string url)
    {
        var u = url.Trim();
        foreach (var prefix in new[] { "https://", "http://", "www." })
        {
            if (u.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                u = u[prefix.Length..];
            }
        }

        return u.TrimEnd('/');
    }

    /// <summary>Escapes LaTeX specials and normalises characters neither engine can typeset. One pass, so no double escaping.</summary>
    public static string Esc(string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "";
        }

        var sb = new StringBuilder(s.Length + 16);
        var quoteOpen = false;
        foreach (var c in s.Trim())
        {
            switch (c)
            {
                case '\\': sb.Append(@"\textbackslash{}"); break;
                case '&': sb.Append(@"\&"); break;
                case '%': sb.Append(@"\%"); break;
                case '$': sb.Append(@"\$"); break;
                case '#': sb.Append(@"\#"); break;
                case '_': sb.Append(@"\_"); break;
                case '{': sb.Append(@"\{"); break;
                case '}': sb.Append(@"\}"); break;
                case '~': sb.Append(@"\textasciitilde{}"); break;
                case '^': sb.Append(@"\textasciicircum{}"); break;
                case '<': sb.Append(@"\textless{}"); break;
                case '>': sb.Append(@"\textgreater{}"); break;
                case '|': sb.Append(@"\textbar{}"); break;
                case '\u2018' or '\u2019' or '\u02BC': sb.Append('\''); break;
                case '\u201C': sb.Append("``"); break;
                case '\u201D': sb.Append("''"); break;
                case '"':
                    // Straight quotes alternate between LaTeX's opening `` and closing ''.
                    sb.Append(quoteOpen ? "''" : "``");
                    quoteOpen = !quoteOpen;
                    break;
                case '\u2013': sb.Append("--"); break;
                case '\u2014': sb.Append("---"); break;
                case '\u2026': sb.Append(@"\ldots{}"); break;
                case '\u20B9': sb.Append("Rs. "); break;
                case '\u2022' or '\u25CF' or '\u25AA' or '\u2023': break; // pasted bullet glyphs
                case '\u00A0' or '\t' or '\r' or '\n': sb.Append(' '); break;
                default:
                    // Everything past Latin-1 (Devanagari, emoji, symbols) would fail under pdfLaTeX or show as a
                    // missing glyph, so it is dropped to keep the resume compiling.
                    if (!char.IsControl(c) && c <= '\u00FF')
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>Inside \href only % and # need escaping. Spaces are removed and a missing scheme becomes https.</summary>
    public static string EscUrl(string url, bool scheme = true)
    {
        var u = new string(url.Trim().Where(c => !char.IsWhiteSpace(c) && c <= '\u007E' && c is not '{' and not '}' and not '\\').ToArray());
        if (scheme && !u.Contains("://", StringComparison.Ordinal))
        {
            u = "https://" + u;
        }

        return u.Replace("%", @"\%", StringComparison.Ordinal).Replace("#", @"\#", StringComparison.Ordinal);
    }
}
