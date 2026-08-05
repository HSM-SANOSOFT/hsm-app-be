namespace Hsm.Infrastructure.Identity;

/// <summary>The scheme a request is authenticated by, chosen per request.</summary>
public static class HsmAuthenticationSchemes
{
    /// <summary>Forwards to bearer when the caller sent one, to the cookie otherwise.</summary>
    public const string Adaptive = "hsm-adaptive";
}
