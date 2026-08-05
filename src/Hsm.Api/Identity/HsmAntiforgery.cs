using Hsm.Application.Errors;
using Microsoft.AspNetCore.Antiforgery;

namespace Hsm.Api.Identity;

/// <summary>
/// Standard ASP.NET antiforgery, applied where it actually protects something:
/// an unsafe method authenticated by an AMBIENT credential (the session
/// cookie). A bearer caller supplies its credential explicitly and cannot be
/// forged into by a third-party page, so demanding a token from integrations
/// would break them to prevent nothing.
///
/// <para><c>UseAntiforgery()</c> alone does not cover this: it validates form
/// posts against endpoint metadata, and this API takes JSON. Validating
/// explicitly here is the wiring, not a reimplementation — <see cref="IAntiforgery"/>
/// still mints, stores and compares the tokens.</para>
/// </summary>
public static class HsmAntiforgery
{
    /// <summary>The session cookie whose presence makes a request forgeable.</summary>
    internal const string SessionCookieName = "hsm.session";

    private static readonly string[] SafeMethods = ["GET", "HEAD", "OPTIONS", "TRACE"];

    public static IApplicationBuilder UseHsmAntiforgery(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.Use(async (context, next) =>
        {
            if (RequiresValidation(context))
            {
                try
                {
                    await context.RequestServices.GetRequiredService<IAntiforgery>()
                        .ValidateRequestAsync(context);
                }
                catch (AntiforgeryValidationException)
                {
                    throw new ForbiddenException("Invalid or missing antiforgery token.");
                }
            }

            await next();
        });
    }

    internal static bool RequiresValidation(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return !SafeMethods.Contains(context.Request.Method, StringComparer.OrdinalIgnoreCase)
            && !context.Request.Headers.Authorization.ToString()
                .StartsWith("Bearer ", StringComparison.Ordinal)
            && context.Request.Cookies.ContainsKey(SessionCookieName);
    }
}
