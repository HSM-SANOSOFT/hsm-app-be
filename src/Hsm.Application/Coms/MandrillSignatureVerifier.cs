using System.Security.Cryptography;
using System.Text;

namespace Hsm.Application.Coms;

/// <summary>
/// Mandrill signature verification: HMAC-SHA1 of the RAW request body bytes
/// with the provider signing key, base64-encoded, compared against the
/// <c>x-mandrill-signature</c> header with a constant-time comparison behind
/// an equal-length guard. No header → invalid.
/// </summary>
public static class MandrillSignatureVerifier
{
    public const string SignatureHeader = "x-mandrill-signature";

    public static bool Verify(string? signature, byte[] rawBody, string signingKey)
    {
        if (string.IsNullOrEmpty(signature))
        {
            return false;
        }

        // CA5350: HMAC-SHA1 is the Mandrill webhook contract's algorithm —
        // the provider computes the signature; this side cannot choose a
        // stronger hash without breaking every real webhook.
#pragma warning disable CA5350
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(signingKey));
#pragma warning restore CA5350
        var computed = Convert.ToBase64String(hmac.ComputeHash(rawBody));

        var computedBytes = Encoding.UTF8.GetBytes(computed);
        var signatureBytes = Encoding.UTF8.GetBytes(signature);
        // FixedTimeEquals is length-guarded internally, but the explicit guard
        // returns false on a length mismatch instead of throwing.
        if (computedBytes.Length != signatureBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(computedBytes, signatureBytes);
    }
}
