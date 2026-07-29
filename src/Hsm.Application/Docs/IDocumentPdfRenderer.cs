namespace Hsm.Application.Docs;

/// <summary>
/// PDF rendering port — replaces the frozen Puppeteer path (plan U15: no
/// browser dependency). Input is the composed template output (HTML); the
/// layout does not need pixel parity with headless Chrome, but the result is
/// a well-formed PDF.
/// </summary>
public interface IDocumentPdfRenderer
{
    byte[] Render(string html);
}
