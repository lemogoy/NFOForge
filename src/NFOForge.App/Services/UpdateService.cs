using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NFOForge.App.Services
{
    public enum UpdateCheckStatus
    {
        UpToDate,
        UpdateAvailable,
        Unavailable
    }

    public sealed record UpdateInfo(
        Version Version,
        Uri DownloadUrl,
        Uri ReleaseNotesUrl,
        string Sha256
    );

    public sealed record UpdateCheckResult(
        UpdateCheckStatus Status,
        UpdateInfo? Update = null
    );

    /// <summary>
    /// Recherche les nouvelles versions de NFOForge
    /// à partir d'un manifeste JSON accessible en HTTPS.
    ///
    /// Ce service ne télécharge et n'exécute aucun logiciel.
    /// </summary>
    public sealed class UpdateService
    {
        private const int MaxManifestSize = 16 * 1024;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _httpClient;
        private readonly Uri _manifestUri;

        public UpdateService(
            HttpClient httpClient,
            Uri manifestUri)
        {
            _httpClient = httpClient
                ?? throw new ArgumentNullException(nameof(httpClient));

            _manifestUri = manifestUri
                ?? throw new ArgumentNullException(nameof(manifestUri));

            if (_manifestUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ArgumentException(
                    "Le manifeste doit utiliser HTTPS.",
                    nameof(manifestUri));
            }
        }

        /// <summary>
        /// Compare la version installée avec la version
        /// déclarée dans le manifeste distant.
        /// </summary>
        public async Task<UpdateCheckResult> CheckAsync(
            Version installedVersion,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(installedVersion);

            using var timeout = CancellationTokenSource
                .CreateLinkedTokenSource(cancellationToken);

            timeout.CancelAfter(TimeSpan.FromSeconds(8));

            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    _manifestUri);

                request.Headers.Accept.ParseAdd(
                    "application/json");

                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    return Unavailable();
                }

                if (response.Content.Headers.ContentLength
                    > MaxManifestSize)
                {
                    return Unavailable();
                }

                await using var stream =
                    await response.Content.ReadAsStreamAsync(
                        timeout.Token).ConfigureAwait(false);

                using var memory = new MemoryStream();

                var buffer = new byte[4096];
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(
                    buffer.AsMemory(),
                    timeout.Token).ConfigureAwait(false)) > 0)
                {
                    if (memory.Length + bytesRead > MaxManifestSize)
                    {
                        return Unavailable();
                    }

                    memory.Write(buffer, 0, bytesRead);
                }

                var manifest =
                    JsonSerializer.Deserialize<UpdateManifest>(
                        memory.ToArray(),
                        JsonOptions);

                if (manifest == null)
                {
                    return Unavailable();
                }

                if (!TryParseVersion(
                    manifest.Version,
                    out var latestVersion))
                {
                    return Unavailable();
                }


                // Comparer les versions avant de demander les
                // informations nécessaires au téléchargement.
                //
                // Un manifeste d'amorçage peut déclarer la version
                // actuelle sans proposer de package d'installation.
                if (latestVersion <= Normalize(installedVersion))
                {
                    return new UpdateCheckResult(
                        UpdateCheckStatus.UpToDate);
                }


                if (!Uri.TryCreate(
                    manifest.DownloadUrl,
                    UriKind.Absolute,
                    out var downloadUri))
                {
                    return Unavailable();
                }

                if (!Uri.TryCreate(
                    manifest.ReleaseNotesUrl,
                    UriKind.Absolute,
                    out var releaseNotesUri))
                {
                    return Unavailable();
                }

                if (downloadUri.Scheme != Uri.UriSchemeHttps ||
                    releaseNotesUri.Scheme != Uri.UriSchemeHttps)
                {
                    return Unavailable();
                }

                var hash = manifest.Sha256?.Trim();

                if (string.IsNullOrEmpty(hash) ||
                    hash.Length != 64 ||
                    !hash.All(Uri.IsHexDigit))
                {
                    return Unavailable();
                }

                
                var update = new UpdateInfo(
                    latestVersion,
                    downloadUri,
                    releaseNotesUri,
                    hash.ToUpperInvariant());

                return new UpdateCheckResult(
                    UpdateCheckStatus.UpdateAvailable,
                    update);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                return Unavailable();
            }
            catch (HttpRequestException)
            {
                return Unavailable();
            }
            catch (IOException)
            {
                return Unavailable();
            }
            catch (JsonException)
            {
                return Unavailable();
            }
        }

        private static bool TryParseVersion(
            string? value,
            out Version version)
        {
            version = new Version(0, 0, 0, 0);

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var text = value.Trim();

            if (text.StartsWith(
                "v",
                StringComparison.OrdinalIgnoreCase))
            {
                text = text[1..];
            }

            if (!Version.TryParse(
                text,
                out var parsed) ||
                parsed.Build < 0)
            {
                return false;
            }

            version = Normalize(parsed);

            return true;
        }

        private static Version Normalize(Version version)
        {
            return new Version(
                version.Major,
                version.Minor,
                Math.Max(version.Build, 0),
                Math.Max(version.Revision, 0));
        }

        private static UpdateCheckResult Unavailable()
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.Unavailable);
        }

        private sealed class UpdateManifest
        {
            public string? Version { get; set; }

            public string? DownloadUrl { get; set; }

            public string? ReleaseNotesUrl { get; set; }

            public string? Sha256 { get; set; }
        }

    }
}