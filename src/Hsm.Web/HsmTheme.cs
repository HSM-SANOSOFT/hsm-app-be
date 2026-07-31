using MudBlazor;

namespace Hsm.Web;

/// <summary>The single MudBlazor theme both layouts render under.</summary>
public static class HsmTheme
{
    public static MudTheme Default { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#1565c0",
            Secondary = "#00897b",
            AppbarBackground = "#1565c0",
        },
    };
}
