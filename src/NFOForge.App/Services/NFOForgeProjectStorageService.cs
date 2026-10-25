using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NFOForge.App.Models;

namespace NFOForge.App.Services
{
    /// <summary>
    /// Lecture et sauvegarde atomique des fichiers .nfoproject (JSON UTF-8 sans BOM).
    /// Aucun contenu n'est exécuté ni téléchargé.
    /// </summary>
    public sealed class NFOForgeProjectStorageService
    {
        public const string ProjectExtension = ".nfoproject";
        private const int CurrentFormatVersion = 1;
        private const int MaxFileBytes = 8 * 1024 * 1024;
        private const int MaxTextLength = 2_000_000;
        private const int MaxMetadataLength = 100_000;
        private const int MaxTemplateNameLength = 100;
        private const int MaxCustomVariables = 100;
        private const int MaxVariableValueLength = 100_000;
        private const int MaxUrlLength = 2048;

        private static readonly UTF8Encoding StrictUtf8 = new(false, true);
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = false,
            MaxDepth = 32
        };
        private static readonly Regex VariableNamePattern = new(
            @"^[A-Za-z][A-Za-z0-9_]*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled,
            TimeSpan.FromSeconds(1));

        public NFOForgeProject Load(string path)
        {
            ValidatePath(path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxFileBytes)
                throw new InvalidDataException("Projet trop volumineux (8 Mio maximum).");

            using var buffer = new MemoryStream();
            byte[] chunk = new byte[81920];
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (buffer.Length + read > MaxFileBytes)
                    throw new InvalidDataException("Projet trop volumineux (8 Mio maximum).");
                buffer.Write(chunk, 0, read);
            }

            byte[] bytes = buffer.ToArray();
            string json;
            try
            {
                json = StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException ex)
            {
                throw new InvalidDataException("Le fichier projet n'est pas en UTF-8 valide.", ex);
            }

            if (json.Length > 0 && json[0] == '\uFEFF')
                throw new InvalidDataException("Le fichier projet doit être en UTF-8 sans BOM.");

            NFOForgeProject project;
            try
            {
                project = JsonSerializer.Deserialize<NFOForgeProject>(json, Options)
                    ?? throw new InvalidDataException("Le document JSON est vide.");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("Format JSON du projet invalide.", ex);
            }

            Validate(project);
            return project;
        }

        /// <summary>
        /// Écrit le projet de manière atomique lorsque le système de fichiers le permet.
        /// Les horodatages sont renseignés sur l'objet après l'écriture réussie.
        /// </summary>
        public void Save(string path, NFOForgeProject project)
        {
            ValidatePath(path);
            ArgumentNullException.ThrowIfNull(project);

            DateTime now = DateTime.UtcNow;
            DateTime originalModifiedAt = project.ModifiedAtUtc;
            project.ModifiedAtUtc = now;
            try
            {
                Validate(project);
                byte[] data = JsonSerializer.SerializeToUtf8Bytes(project, Options);
                if (data.Length > MaxFileBytes)
                    throw new InvalidDataException("Projet trop volumineux (8 Mio maximum).");

                string fullPath = Path.GetFullPath(path);
                string directory = Path.GetDirectoryName(fullPath)
                    ?? throw new IOException("Dossier de projet invalide.");
                Directory.CreateDirectory(directory);
                string tempPath = Path.Combine(directory,
                    "." + Guid.NewGuid().ToString("N") + ".tmp");

                try
                {
                    using (var output = new FileStream(tempPath, FileMode.CreateNew,
                        FileAccess.Write, FileShare.None))
                    {
                        output.Write(data);
                        output.Flush(flushToDisk: true);
                    }

                    if (File.Exists(fullPath))
                        File.Replace(tempPath, fullPath, null);
                    else
                        File.Move(tempPath, fullPath);
                }
                finally
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
            }
            catch
            {
                project.ModifiedAtUtc = originalModifiedAt;
                throw;
            }
        }

        public static void Validate(NFOForgeProject project)
        {
            ArgumentNullException.ThrowIfNull(project);
            if (project.FormatVersion != CurrentFormatVersion)
                throw new InvalidDataException(
                    $"Version de projet non prise en charge : {project.FormatVersion}.");
            if (!Guid.TryParseExact(project.ProjectId, "N", out _))
                throw new InvalidDataException("Identifiant de projet invalide.");
            if (project.CreatedAtUtc.Kind != DateTimeKind.Utc ||
                project.ModifiedAtUtc.Kind != DateTimeKind.Utc ||
                project.CreatedAtUtc > project.ModifiedAtUtc)
                throw new InvalidDataException("Horodatages UTC invalides.");
            if (project.Metadata is null)
                throw new InvalidDataException("Métadonnées de projet absentes.");

            CheckText(project.NfoText, "NFO", MaxTextLength);
            CheckText(project.BBCodeText, "BBCode", MaxTextLength);
            CheckText(project.NfoTemplate, "Modèle NFO", MaxTemplateNameLength);
            CheckText(project.BBCodeTemplate, "Modèle BBCode", MaxTemplateNameLength);
            if (string.IsNullOrWhiteSpace(project.NfoTemplate) ||
                string.IsNullOrWhiteSpace(project.BBCodeTemplate))
                throw new InvalidDataException("Un nom de modèle est obligatoire.");
            if (project.NfoEncoding is not ("CP437" or "UTF-8 sans BOM" or "UTF-8 avec BOM"))
                throw new InvalidDataException("Encodage NFO non reconnu.");

            ProjectMetadata m = project.Metadata;
            CheckText(m.Name, nameof(m.Name), MaxMetadataLength);
            CheckText(m.Version, nameof(m.Version), MaxMetadataLength);
            CheckText(m.Publisher, nameof(m.Publisher), MaxMetadataLength);
            CheckText(m.Developer, nameof(m.Developer), MaxMetadataLength);
            CheckText(m.Category, nameof(m.Category), MaxMetadataLength);
            CheckText(m.Subcategory, nameof(m.Subcategory), MaxMetadataLength);
            CheckText(m.PublicationProfile, nameof(m.PublicationProfile), MaxMetadataLength);
            CheckText(m.OperatingSystem, nameof(m.OperatingSystem), MaxMetadataLength);
            CheckText(m.Architecture, nameof(m.Architecture), MaxMetadataLength);
            CheckText(m.Language, nameof(m.Language), MaxMetadataLength);
            CheckText(m.Description, nameof(m.Description), MaxMetadataLength);
            CheckText(m.Features, nameof(m.Features), MaxMetadataLength);
            CheckText(m.AdditionalInformation, nameof(m.AdditionalInformation), MaxMetadataLength);

            ValidateUrl(project.PosterUrl, "affiche");
            ValidateUrl(project.ScreenshotUrl, "capture");
            if (project.BBCodeCustomValues is null ||
                project.BBCodeCustomValues.Count > MaxCustomVariables)
                throw new InvalidDataException("Nombre de variables BBCode invalide.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in project.BBCodeCustomValues)
            {
                if (pair.Key.Length > 100 || !VariableNamePattern.IsMatch(pair.Key) ||
                    !names.Add(pair.Key))
                    throw new InvalidDataException("Nom de variable personnalisé invalide ou en double.");
                CheckText(pair.Value, "Valeur de " + pair.Key, MaxVariableValueLength);
                if (pair.Key.EndsWith("_URL", StringComparison.OrdinalIgnoreCase))
                    ValidateUrl(pair.Value, pair.Key);
            }
        }

        private static void CheckText(string? value, string label, int max)
        {
            if (value is null || value.Length > max)
                throw new InvalidDataException($"Champ {label} invalide ou trop long.");
        }

        private static void ValidateUrl(string? value, string label)
        {
            CheckText(value, label, MaxUrlLength);
            if (string.IsNullOrWhiteSpace(value)) return;
            if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidDataException($"L'URL ({label}) doit être HTTP ou HTTPS.");
        }

        private static void ValidatePath(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (!string.Equals(Path.GetExtension(path), ProjectExtension,
                StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Extension .nfoproject obligatoire.", nameof(path));
        }
    }
}
