using System.Text.RegularExpressions;
using Hsm.Application.Docs;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Hsm.Infrastructure.Docs;

/// <summary>
/// QuestPDF adapter for <see cref="IDocumentPdfRenderer"/> — the U15
/// replacement for the frozen Puppeteer/headless-Chrome path (the reason the
/// Chrome dependencies left the devcontainer in U5). The frozen page setup is
/// mirrored where it maps (A4, ~20px margins — Puppeteer hardcoded A4
/// regardless of the template's size/orientation metadata); the HTML itself
/// is rendered as text content, not a pixel-parity browser layout (plan:
/// inputs/outputs/persistence must match, layout need not).
/// </summary>
public sealed partial class QuestPdfDocumentRenderer : IDocumentPdfRenderer
{
    static QuestPdfDocumentRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] Render(string html)
    {
        var lines = ExtractLines(html);
        return Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(15);
            page.DefaultTextStyle(style => style.FontSize(11));
            page.Content().Column(column =>
            {
                foreach (var line in lines)
                {
                    column.Item().Text(line);
                }
            });
        })).GeneratePdf();
    }

    /// <summary>Flattens the rendered template HTML into text lines.</summary>
    private static List<string> ExtractLines(string html)
    {
        var text = BlockBoundaryPattern().Replace(html, "\n");
        text = TagPattern().Replace(text, string.Empty);
        text = System.Net.WebUtility.HtmlDecode(text);

        var lines = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length > 0)
            {
                lines.Add(line);
            }
        }

        if (lines.Count == 0)
        {
            lines.Add(string.Empty);
        }

        return lines;
    }

    [GeneratedRegex(@"<\s*(?:br|/p|/div|/h[1-6]|/li|/tr)\s*/?\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockBoundaryPattern();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex TagPattern();
}
