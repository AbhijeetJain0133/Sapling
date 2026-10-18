using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Sapling.Web.Services;

public interface IPdfTextExtractor
{
    /// <summary>Plain text of the first pages, whitespace collapsed; empty when the PDF has no text layer or cannot be read.</summary>
    string Extract(byte[] bytes, int maxPages = 8);
}

public sealed partial class PdfTextExtractor(ILogger<PdfTextExtractor> log) : IPdfTextExtractor
{
    public string Extract(byte[] bytes, int maxPages = 8)
    {
        try
        {
            using var document = PdfDocument.Open(bytes);
            var text = new StringBuilder();
            foreach (var page in document.GetPages().Take(maxPages))
            {
                text.AppendLine(ContentOrderTextExtractor.GetText(page));
                text.AppendLine();
            }

            return Blank().Replace(Spaces().Replace(text.ToString(), " "), "\n\n").Trim();
        }
        catch (Exception e)
        {
            // Encrypted or malformed files: the interview simply continues without the resume.
            log.LogInformation(e, "Could not read text from an uploaded PDF.");
            return "";
        }
    }

    [GeneratedRegex(@"[ \t\f\v]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\s*\n\s*\n\s*")]
    private static partial Regex Blank();
}
