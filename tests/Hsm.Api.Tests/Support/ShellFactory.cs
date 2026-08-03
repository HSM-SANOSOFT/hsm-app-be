using Microsoft.AspNetCore.Hosting;

namespace Hsm.Api.Tests;

/// <summary>
/// The staff door. Shell suites boot Hsm.Web — Blazor, screens, UI services,
/// no REST — and reach the API surface through a SIDECAR Hsm.Api host on the
/// same database, behind one client that routes by path. That is the
/// deployment in miniature: sign in through either door and the other
/// recognizes you.
/// </summary>
public abstract class ShellFactory : ApiHostFactory<Hsm.Web.Program>
{
    private static readonly string[] ApiPrefixes = ["/api", "/fhir", "/v1"];

    private readonly ApiSurfaceFactory _apiSurface;

    protected ShellFactory() => _apiSurface = new ApiSurfaceFactory(this);

    public override HttpClient CreateApiClient() =>
        new(new SurfaceRouter(_apiSurface.Server.CreateHandler(), Server.CreateHandler(), ApiPrefixes))
        {
            BaseAddress = new Uri("http://localhost"),
        };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _apiSurface.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>The REST door, on the shell factory's database and settings.</summary>
    private sealed class ApiSurfaceFactory(ShellFactory shell) : ApiHostFactory<Hsm.Api.Program>
    {
        protected override string DatabaseName => shell.Database;

        protected override string JobKeyPrefix => shell.JobNamespace;

        protected override void ConfigureModule(IWebHostBuilder builder) =>
            shell.ApplyModuleConfiguration(builder);
    }

    /// <summary>Path-prefix routing across the two in-memory hosts.</summary>
    private sealed class SurfaceRouter : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _api;
        private readonly HttpMessageInvoker _shell;
        private readonly string[] _apiPrefixes;

        public SurfaceRouter(HttpMessageHandler api, HttpMessageHandler shell, string[] apiPrefixes)
        {
            _api = new HttpMessageInvoker(api);
            _shell = new HttpMessageInvoker(shell);
            _apiPrefixes = apiPrefixes;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "/";
            var target = _apiPrefixes.Any(p => path.StartsWith(p, StringComparison.Ordinal))
                ? _api
                : _shell;
            return target.SendAsync(request, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _api.Dispose();
                _shell.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
