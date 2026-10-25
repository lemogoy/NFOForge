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
    public sealed class BBCodeTemplateStorageService
    {
        private const int MaxTemplates = 100;
        private const int MaxContentLength = 100_000;
        private const int MaxStorageBytes = 12_000_000;

        private static readonly object Sync = new();

        private static readonly string[] ReservedNames =
        [
            "Classique",
            "Moderne"
        ];

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static readonly Regex VariablePattern = new(
            @"\{([A-Za-z][A-Za-z0-9]*)\}",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        private readonly string _filePath;

        public BBCodeTemplateStorageService()
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "NFOForge",
                "Templates");

            _filePath = Path.Combine(
                directory,
                "bbcode-templates.json");
        }

        public IReadOnlyList<BBCodeTemplate> Load()
        {
            lock (Sync)
            {
                return ReadTemplates()
                    .Select(Clone)
                    .ToList();
            }
        }

        public BBCodeTemplate Create(
            string name,
            string content)
        {
            lock (Sync)
            {
                List<BBCodeTemplate> templates = ReadTemplates();

                if (templates.Count >= MaxTemplates)
                    throw new InvalidOperationException(
                        "Nombre maximal de modèles atteint.");

                var template = new BBCodeTemplate
                {
                    Name = name,
                    Content = content,
                    IsBuiltIn = false
                };

                Validate(template);
                EnsureUniqueName(templates, template.Name, null);

                templates.Add(template);
                SaveTemplates(templates);

                return Clone(template);
            }
        }

        public void Update(BBCodeTemplate template)
        {
            ArgumentNullException.ThrowIfNull(template);

            lock (Sync)
            {
                List<BBCodeTemplate> templates = ReadTemplates();

                int index = templates.FindIndex(
                    item => string.Equals(
                        item.Id,
                        template.Id,
                        StringComparison.OrdinalIgnoreCase));

                if (index < 0)
                    throw new InvalidOperationException(
                        "Modèle BBCode introuvable.");

                if (template.IsBuiltIn)
                    throw new InvalidOperationException(
                        "Modification d'un modèle intégré interdite.");

                var updated = new BBCodeTemplate
                {
                    Id = templates[index].Id,
                    Name = template.Name,
                    Content = template.Content,
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
                List<BBCodeTemplate> templates = ReadTemplates();

                BBCodeTemplate? template = templates.FirstOrDefault(
                    item => string.Equals(
                        item.Id,
                        id,
                        StringComparison.OrdinalIgnoreCase));

                if (template is null)
                    throw new InvalidOperationException(
                        "Modèle BBCode introuvable.");

                if (template.IsBuiltIn)
                    throw new InvalidOperationException(
                        "Suppression d'un modèle intégré interdite.");

                templates.Remove(template);
                SaveTemplates(templates);
            }
        }


        // Un fichier partagé contient un seul modèle personnalisé.
        // L'importation crée toujours un nouvel identifiant : aucun écrasement.
        public BBCodeTemplate ImportFromFile(string sourcePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
            string fullPath = Path.GetFullPath(sourcePath);
            if (!string.Equals(Path.GetExtension(fullPath), ".json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Seuls les modèles JSON sont acceptés.");

            lock (Sync)
            {
                var file = new FileInfo(fullPath);
                if (!file.Exists)
                    throw new FileNotFoundException("Fichier de modèle introuvable.", fullPath);
                if (file.Length > MaxStorageBytes)
                    throw new InvalidDataException("Fichier de modèle trop volumineux.");

                string json = File.ReadAllText(fullPath, new UTF8Encoding(false, true));
                BBCodeTemplate incoming = JsonSerializer.Deserialize<BBCodeTemplate>(json, JsonOptions)
                    ?? throw new InvalidDataException("Modèle JSON invalide.");
                Validate(incoming); // Refuse notamment IsBuiltIn, noms réservés et variables inconnues.

                List<BBCodeTemplate> templates = ReadTemplates();
                if (templates.Count >= MaxTemplates)
                    throw new InvalidOperationException("Nombre maximal de modèles atteint.");
                EnsureUniqueName(templates, incoming.Name, null);

                var created = new BBCodeTemplate
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = incoming.Name.Trim(),
                    Content = incoming.Content,
                    IsBuiltIn = false
                };
                templates.Add(created);
                SaveTemplates(templates);
                return Clone(created);
            }
        }

        public void ExportToFile(string templateId, string destinationPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
            ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
            string fullPath = Path.GetFullPath(destinationPath);
            if (!string.Equals(Path.GetExtension(fullPath), ".json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("L'export doit utiliser l'extension .json.");
            if (string.Equals(fullPath, Path.GetFullPath(_filePath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Le fichier interne de la bibliothèque ne peut pas être remplacé.");

            lock (Sync)
            {
                BBCodeTemplate template = ReadTemplates().FirstOrDefault(t =>
                    string.Equals(t.Id, templateId, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("Modèle personnalisé introuvable.");
                Validate(template);
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(template, JsonOptions);
                if (bytes.Length > MaxStorageBytes)
                    throw new InvalidDataException("Export trop volumineux.");

                string directory = Path.GetDirectoryName(fullPath)
                    ?? throw new IOException("Dossier de destination invalide.");
                string temp = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        stream.Write(bytes);
                        stream.Flush(flushToDisk: true);
                    }
                    if (File.Exists(fullPath))
                        File.Replace(temp, fullPath, null);
                    else
                        File.Move(temp, fullPath);
                }
                finally
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
            }
        }

        private List<BBCodeTemplate> ReadTemplates()
        {
            if (!File.Exists(_filePath))
                return new List<BBCodeTemplate>();

            if (new FileInfo(_filePath).Length > MaxStorageBytes)
                throw new InvalidDataException(
                    "Fichier de modèles trop volumineux.");

            string json = File.ReadAllText(
                _filePath,
                new UTF8Encoding(false, true));

            List<BBCodeTemplate> templates =
                JsonSerializer.Deserialize<List<BBCodeTemplate>>(
                    json,
                    JsonOptions)
                ?? throw new InvalidDataException(
                    "Fichier JSON invalide.");

            ValidateCollection(templates);

            return templates;
        }

        private void SaveTemplates(List<BBCodeTemplate> templates)
        {
            ValidateCollection(templates);

            string directory = Path.GetDirectoryName(_filePath)
                ?? throw new IOException(
                    "Dossier de stockage invalide.");

            Directory.CreateDirectory(directory);

            byte[] data = JsonSerializer.SerializeToUtf8Bytes(
                templates,
                JsonOptions);

            if (data.Length > MaxStorageBytes)
                throw new InvalidDataException(
                    "Limite de stockage dépassée.");

            string temporaryPath = Path.Combine(
                directory,
                "." + Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                {
                    stream.Write(data);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(_filePath))
                {
                    File.Replace(
                        temporaryPath,
                        _filePath,
                        null);
                }
                else
                {
                    File.Move(temporaryPath, _filePath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        private static void ValidateCollection(
            List<BBCodeTemplate> templates)
        {
            if (templates.Count > MaxTemplates)
                throw new InvalidDataException(
                    "Trop de modèles enregistrés.");

            var ids = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            var names = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (BBCodeTemplate template in templates)
            {
                if (template is null)
                    throw new InvalidDataException(
                        "Modèle nul dans le stockage.");

                Validate(template);

                if (!ids.Add(template.Id))
                    throw new InvalidDataException(
                        "Identifiant de modèle en double.");

                if (!names.Add(template.Name.Trim()))
                    throw new InvalidDataException(
                        "Nom de modèle en double.");
            }
        }

        private static void Validate(BBCodeTemplate template)
        {
            if (!Guid.TryParseExact(template.Id, "N", out _))
                throw new InvalidDataException(
                    "Identifiant de modèle invalide.");

            if (string.IsNullOrWhiteSpace(template.Name) ||
                template.Name.Length > 100)
                throw new InvalidDataException(
                    "Le nom doit contenir entre 1 et 100 caractères.");

            if (ReservedNames.Any(name =>
                string.Equals(
                    name,
                    template.Name.Trim(),
                    StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException(
                    "Nom réservé à un modèle intégré.");
            }

            if (template.IsBuiltIn)
                throw new InvalidDataException(
                    "Un modèle intégré ne peut pas être enregistré ici.");

            if (template.Content is null ||
                template.Content.Length > MaxContentLength)
            {
                throw new InvalidDataException(
                    "Contenu de modèle invalide ou trop volumineux.");
            }

            try
            {
                new BBCodeTemplateService()
                    .ValidateVariables(template.Content);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidDataException(
                    "Contenu du modèle invalide : " + ex.Message,
                    ex);
            }
        }

        private static void EnsureUniqueName(
            IEnumerable<BBCodeTemplate> templates,
            string name,
            string? excludedId)
        {
            if (templates.Any(item =>
                !string.Equals(
                    item.Id,
                    excludedId,
                    StringComparison.OrdinalIgnoreCase)
                &&
                string.Equals(
                    item.Name.Trim(),
                    name.Trim(),
                    StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "Un modèle portant ce nom existe déjà.");
            }
        }

        private static BBCodeTemplate Clone(BBCodeTemplate template)
        {
            return new BBCodeTemplate
            {
                Id = template.Id,
                Name = template.Name,
                Content = template.Content,
                IsBuiltIn = template.IsBuiltIn
            };
        }
    }
}