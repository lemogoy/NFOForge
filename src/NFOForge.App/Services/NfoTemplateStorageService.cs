using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NFOForge.App.Models;

namespace NFOForge.App.Services
{
    public sealed class NfoTemplateStorageService
    {
        private const int MaxTemplates = 100;
        private const int MaxContentLength = 100_000;
        private const int MaxStorageBytes = 12_000_000;

        // Synchronisation des instances du service au sein du même processus.
        private static readonly object Sync = new();
        private static readonly HashSet<string> ReservedNames = new(
            StringComparer.OrdinalIgnoreCase) { "Classique", "Moderne", "ASCII Art" };

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static readonly Regex VariablePattern = new(
            @"\{([A-Za-z][A-Za-z0-9]*)\}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        private readonly string _filePath;

        public NfoTemplateStorageService()
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NFOForge", "Templates");
            _filePath = Path.Combine(directory, "nfo-templates.json");
        }

        public IReadOnlyList<NfoTemplate> Load()
        {
            lock (Sync) return ReadTemplates().Select(Clone).ToArray();
        }

        public NfoTemplate Create(string name, string content)
        {
            lock (Sync)
            {
                List<NfoTemplate> templates = ReadTemplates();
                if (templates.Count >= MaxTemplates)
                    throw new InvalidOperationException("Nombre maximal de modèles atteint.");

                var created = new NfoTemplate
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = name?.Trim() ?? string.Empty,
                    Content = content ?? string.Empty,
                    IsBuiltIn = false
                };
                Validate(created);
                EnsureUniqueName(templates, created.Name);
                templates.Add(created);
                SaveTemplates(templates);
                return Clone(created);
            }
        }

        public void Update(NfoTemplate template)
        {
            ArgumentNullException.ThrowIfNull(template);
            lock (Sync)
            {
                List<NfoTemplate> templates = ReadTemplates();
                int index = templates.FindIndex(t =>
                    string.Equals(t.Id, template.Id, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                    throw new InvalidOperationException("Modèle personnalisé introuvable.");
                if (templates[index].IsBuiltIn || template.IsBuiltIn)
                    throw new InvalidOperationException("Un modèle intégré ne peut pas être modifié.");

                var updated = new NfoTemplate
                {
                    Id = templates[index].Id,
                    Name = template.Name?.Trim() ?? string.Empty,
                    Content = template.Content ?? string.Empty,
                    IsBuiltIn = false
                };
                Validate(updated);
                EnsureUniqueName(templates, updated.Name, updated.Id);
                templates[index] = updated;
                SaveTemplates(templates);
            }
        }

        public void Delete(string id)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            lock (Sync)
            {
                List<NfoTemplate> templates = ReadTemplates();
                NfoTemplate? template = templates.FirstOrDefault(t =>
                    string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
                if (template is null)
                    throw new InvalidOperationException("Modèle personnalisé introuvable.");
                if (template.IsBuiltIn)
                    throw new InvalidOperationException("Impossible de supprimer un modèle intégré.");
                templates.Remove(template);
                SaveTemplates(templates);
            }
        }

        private List<NfoTemplate> ReadTemplates()
        {
            if (!File.Exists(_filePath)) return new List<NfoTemplate>();
            var info = new FileInfo(_filePath);
            if (info.Length > MaxStorageBytes)
                throw new InvalidDataException("Le fichier des modèles est trop volumineux.");

            try
            {
                string json = File.ReadAllText(_filePath, new UTF8Encoding(false, true));
                List<NfoTemplate> templates =
                    JsonSerializer.Deserialize<List<NfoTemplate>>(json, JsonOptions)
                    ?? throw new InvalidDataException("Le JSON des modèles est invalide.");
                ValidateCollection(templates);
                return templates;
            }
            catch (JsonException ex)
            {
                // Ne jamais réinitialiser silencieusement un fichier corrompu.
                throw new InvalidDataException(
                    "Le JSON des modèles est corrompu. Fichier conservé sans écriture.", ex);
            }
            catch (DecoderFallbackException ex)
            {
                throw new InvalidDataException(
                    "Le fichier des modèles ne contient pas d'UTF-8 valide.", ex);
            }
        }

        private void SaveTemplates(List<NfoTemplate> templates)
        {
            ValidateCollection(templates);
            string directory = Path.GetDirectoryName(_filePath)
                ?? throw new IOException("Dossier de stockage invalide.");
            Directory.CreateDirectory(directory);

            byte[] data = JsonSerializer.SerializeToUtf8Bytes(templates, JsonOptions);
            if (data.Length > MaxStorageBytes)
                throw new InvalidDataException("Le stockage des modèles dépasse la limite.");

            string temporaryPath = Path.Combine(directory,
                "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None))
                {
                    stream.Write(data);
                    stream.Flush(flushToDisk: true);
                }
                if (File.Exists(_filePath))
                    File.Replace(temporaryPath, _filePath, null);
                else
                    File.Move(temporaryPath, _filePath);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        private static void EnsureUniqueName(
            List<NfoTemplate> templates, string name, string? exceptId = null)
        {
            if (templates.Any(t =>
                !string.Equals(t.Id, exceptId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(t.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Un modèle portant ce nom existe déjà.");
        }

        private static void ValidateCollection(List<NfoTemplate> templates)
        {
            if (templates.Count > MaxTemplates)
                throw new InvalidDataException("Trop de modèles enregistrés.");

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (NfoTemplate template in templates)
            {
                if (template is null)
                    throw new InvalidDataException("Un modèle enregistré est nul.");
                Validate(template);
                if (!ids.Add(template.Id))
                    throw new InvalidDataException("Identifiants de modèles en double.");
                if (!names.Add(template.Name.Trim()))
                    throw new InvalidDataException("Noms de modèles en double.");
                if (template.IsBuiltIn)
                    throw new InvalidDataException("Un modèle intégré ne doit pas être enregistré ici.");
            }
        }

        private static void Validate(NfoTemplate template)
        {
            if (!Guid.TryParseExact(template.Id, "N", out _))
                throw new InvalidDataException("Identifiant de modèle invalide.");
            if (string.IsNullOrWhiteSpace(template.Name) ||
                template.Name.Length > 100 || template.Name != template.Name.Trim())
                throw new InvalidDataException("Le nom doit contenir de 1 à 100 caractères sans espaces périphériques.");
            if (ReservedNames.Contains(template.Name))
                throw new InvalidDataException("Ce nom est réservé à un modèle intégré.");
            if (template.Content is null || template.Content.Length > MaxContentLength)
                throw new InvalidDataException("Contenu de modèle invalide ou trop volumineux.");

            var available = new HashSet<string>(
                new NfoTemplateService().GetAvailableVariables(),
                StringComparer.OrdinalIgnoreCase);
            foreach (Match match in VariablePattern.Matches(template.Content))
            {
                string variable = match.Groups[1].Value;
                if (!available.Contains(variable))
                    throw new InvalidDataException("Variable NFO inconnue : " + variable);
            }
        }

        private static NfoTemplate Clone(NfoTemplate template) => new()
        {
            Id = template.Id,
            Name = template.Name,
            Content = template.Content,
            IsBuiltIn = template.IsBuiltIn
        };
    }
}