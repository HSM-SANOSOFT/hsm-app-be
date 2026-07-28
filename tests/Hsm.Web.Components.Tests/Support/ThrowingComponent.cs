using Microsoft.AspNetCore.Components;

namespace Hsm.Web.Components.Tests;

/// <summary>A page body that fails the way a buggy component does.</summary>
public sealed class ThrowingComponent : ComponentBase
{
    public const string Message = "Componente averiado (escenario de prueba).";

    protected override void OnInitialized() => throw new InvalidOperationException(Message);
}
