using Bunit;
using Hsm.Contracts.Ui;
using Hsm.Web.Components.Pages.Admin;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Web.Components.Tests;

/// <summary>
/// The document management screen (plan U18, screen 5): the listing renders,
/// upload drives the UI service with the selected file, and the retrieve and
/// delete actions call through per row.
/// </summary>
public sealed class DocumentsPageTests : MudTestContext
{
    private static DocumentRowDto Doc(string title) => new(
        Guid.NewGuid().ToString(), title, "UPLOADED", "UPLOADED", DateTimeOffset.UtcNow);

    [Fact]
    public void Renders_the_document_listing()
    {
        var fake = new FakeDocumentsAdminUiService
        {
            Documents = [Doc("informe-enero.pdf"), Doc("acta-directiva.pdf")],
        };
        Services.AddSingleton<IDocumentsAdminUiService>(fake);

        var cut = Render<Documents>();

        Assert.Contains("Documentos", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("informe-enero.pdf", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("acta-directiva.pdf", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Upload_invokes_the_service_with_the_selected_file()
    {
        var fake = new FakeDocumentsAdminUiService();
        Services.AddSingleton<IDocumentsAdminUiService>(fake);

        var cut = Render<Documents>();
        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromText("hola mundo", "reporte.txt"));
        cut.Find("button[data-testid='upload-submit']").Click();

        Assert.Equal(["reporte.txt"], fake.UploadedFileNames);
        // The listing refreshed and shows the uploaded document.
        Assert.Contains("reporte.txt", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Download_asks_the_service_for_the_presigned_url()
    {
        var document = Doc("informe-enero.pdf");
        var fake = new FakeDocumentsAdminUiService { Documents = [document] };
        Services.AddSingleton<IDocumentsAdminUiService>(fake);

        var cut = Render<Documents>();
        cut.Find($"button[data-testid='open-document-{document.Id}']").Click();

        Assert.Equal([document.Id], fake.UrlCalls);
    }

    [Fact]
    public void Delete_invokes_the_service_and_refreshes_the_listing()
    {
        var document = Doc("informe-enero.pdf");
        var fake = new FakeDocumentsAdminUiService { Documents = [document] };
        Services.AddSingleton<IDocumentsAdminUiService>(fake);

        var cut = Render<Documents>();
        cut.Find($"button[data-testid='delete-document-{document.Id}']").Click();

        Assert.Equal([document.Id], fake.DeleteCalls);
        Assert.DoesNotContain("informe-enero.pdf", cut.Markup, StringComparison.Ordinal);
    }
}
