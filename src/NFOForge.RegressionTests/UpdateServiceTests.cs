using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NFOForge.App.Services;

internal static class UpdateServiceTests
{
    private static readonly Uri ManifestUri =
        new("https://updates.example.org/update.json");

    private const string ValidHash =
        "0123456789abcdef0123456789abcdef" +
        "0123456789abcdef0123456789abcdef";

    public static void Run(Action<bool, string> check)
    {
        ArgumentNullException.ThrowIfNull(check);

        string Manifest(
            string version = "1.0.2",
            string? hash = null,
            string downloadUrl =
                "https://updates.example.org/NFOForge-Setup.exe")
        {
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                version,
                downloadUrl,
                releaseNotesUrl =
                    "https://updates.example.org/releases/1.0.2",
                sha256 = hash ?? ValidHash
            });
        }

        UpdateCheckResult Execute(
            string json,
            Version? installed = null)
        {
            using var client = new HttpClient(
                new FakeHttpHandler(
                    _ => new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            json,
                            Encoding.UTF8,
                            "application/json")
                    }));

            var service = new UpdateService(client, ManifestUri);

            return service.CheckAsync(
                installed ?? new Version(1, 0, 1))
                .GetAwaiter()
                .GetResult();
        }

        // 1. Nouvelle version.
        var result = Execute(Manifest());
        check(result.Status == UpdateCheckStatus.UpdateAvailable,
            "Mise à jour 1.0.2 non détectée.");
        check(result.Update?.Version == new Version(1, 0, 2, 0),
            "Version distante incorrecte.");

        // 2. Même version.
        result = Execute(Manifest("1.0.1"));
        check(result.Status == UpdateCheckStatus.UpToDate,
            "Version identique signalée comme mise à jour.");

        // 3. Version distante inférieure.
        result = Execute(Manifest("1.0.0"));
        check(result.Status == UpdateCheckStatus.UpToDate,
            "Ancienne version proposée.");

        // 4. JSON invalide.
        result = Execute("{ json invalide");
        check(result.Status == UpdateCheckStatus.Unavailable,
            "JSON invalide accepté.");

        // 5. Empreinte incorrecte.
        result = Execute(Manifest(hash: "1234"));
        check(result.Status == UpdateCheckStatus.Unavailable,
            "Empreinte SHA-256 invalide acceptée.");

        // 6. URL de téléchargement HTTP.
        result = Execute(Manifest(
            downloadUrl: "http://updates.example.org/setup.exe"));
        check(result.Status == UpdateCheckStatus.Unavailable,
            "Téléchargement HTTP non sécurisé accepté.");

        // 7. Manifeste supérieur à 16 Ko.
        result = Execute(Manifest() + new string(' ', 17000));
        check(result.Status == UpdateCheckStatus.Unavailable,
            "Manifeste trop volumineux accepté.");

        // 8. Version distante invalide.
        result = Execute(Manifest("version-inconnue"));
        check(result.Status == UpdateCheckStatus.Unavailable,
            "Version incorrecte acceptée.");

        // 9. Refus d'une URL de manifeste HTTP.
        using (var client = new HttpClient(new FakeHttpHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK))))
        {
            bool rejected = false;

            try
            {
                _ = new UpdateService(
                    client,
                    new Uri("http://updates.example.org/update.json"));
            }
            catch (ArgumentException)
            {
                rejected = true;
            }

            check(rejected, "Manifeste HTTP accepté.");
        }

        // 10. Échec réseau simulé.
        using (var client = new HttpClient(
            new FakeHttpHandler(
                _ => throw new HttpRequestException(
                    "Connexion indisponible"))))
        {
            var service = new UpdateService(client, ManifestUri);

            result = service.CheckAsync(new Version(1, 0, 1))
                .GetAwaiter()
                .GetResult();

            check(result.Status == UpdateCheckStatus.Unavailable,
                "Erreur réseau non gérée.");
        }

        // 11. Manifeste d'amorçage sans téléchargement.
        const string bootstrapManifest = """
        {
            "version": "1.0.1",
            "status": "stable",
            "downloadUrl": null,
            "releaseNotesUrl": null,
            "sha256": null
        }
        """;

        result = Execute(bootstrapManifest);

        check(
            result.Status == UpdateCheckStatus.UpToDate,
            "Manifeste d'amorçage refusé pour une version identique.");

        // 12. Nouvelle version sans package :
        // ne jamais proposer de mise à jour incomplète.
        const string incompleteManifest = """
        {
            "version": "1.0.2",
            "status": "stable",
            "downloadUrl": null,
            "releaseNotesUrl": null,
            "sha256": null
        }
        """;

        result = Execute(incompleteManifest);

        check(
            result.Status == UpdateCheckStatus.Unavailable,
            "Nouvelle version sans package incorrectement acceptée.");

    }

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage>
            _responseFactory;

        public FakeHttpHandler(
            Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(_responseFactory(request));
        }
    }
}