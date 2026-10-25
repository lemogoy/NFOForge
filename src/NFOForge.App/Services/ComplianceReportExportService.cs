using System;
using System.IO;
using System.Text;
using System.Text.Json;
using NFOForge.App.Models;
using NFOForge.Compliance;

namespace NFOForge.App.Services
{
    /// <summary>
    /// Exportation d'un rapport de conformité au format JSON.
    /// Le service ne modifie pas les fichiers analysés.
    /// </summary>
    public sealed class ComplianceReportExportService
    {
        public void ExportJson(
            string destinationPath,
            SoftwareMetadata metadata,
            ComplianceReport report,
            ReleaseInventoryResult? inventory,
            string displayedStatus)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(report);

            var document = new
            {
                Application = "NFOForge",
                ApplicationVersion = "1.0.0",
                Developer = "Guidessence",

                ReportGeneratedAtUtc = DateTimeOffset.UtcNow,

                PublicationProfile = metadata.PublicationProfile,
                Category = metadata.Category,
                Subcategory = metadata.Subcategory,

                Software = new
                {
                    metadata.Name,
                    metadata.Version,
                    metadata.Publisher,
                    metadata.Developer,
                    metadata.OperatingSystem,
                    metadata.Architecture,
                    metadata.Language,
                    metadata.ReleaseDate,
                    metadata.Description
                },

                Compliance = new
                {
                    DisplayedStatus = displayedStatus,
                    EngineStatus = report.Status.ToString(),
                    report.RuleSetVersion,

                    // Aucun export conforme n'est actuellement autorisé.
                    AllowConformingExport = false,

                    Findings = report.Findings
                },

                Inventory = inventory is null
                    ? null
                    : new
                    {
                        inventory.FolderPath,
                        inventory.FileCount,
                        inventory.TotalSizeBytes,
                        inventory.IsComplete,
                        inventory.SkippedDirectories,
                        inventory.SkippedFiles,
                        inventory.Warnings,
                        inventory.Files
                    },

                Disclaimer =
                    "Ce rapport constitue une analyse locale. " +
                    "Il ne garantit ni l'absence de logiciel malveillant " +
                    "ni l'acceptation de la release par la plateforme."
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(document, options);

            File.WriteAllText(
                destinationPath,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        public void ExportTxt(
    string destinationPath,
    SoftwareMetadata metadata,
    ComplianceReport report,
    ReleaseInventoryResult? inventory,
    string displayedStatus)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(report);

            var sb = new StringBuilder();

            sb.AppendLine("==================================================");
            sb.AppendLine("             NFOFORGE - RAPPORT C411");
            sb.AppendLine("==================================================");
            sb.AppendLine();

            sb.AppendLine("APPLICATION");
            sb.AppendLine("--------------------------------------------------");
            sb.AppendLine("Logiciel       : NFOForge");
            sb.AppendLine("Version       : 1.0.0");
            sb.AppendLine("Développeur   : Guidessence");
            sb.AppendLine($"Date UTC      : {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            sb.AppendLine("MÉTADONNÉES");
            sb.AppendLine("--------------------------------------------------");
            sb.AppendLine($"Nom           : {metadata.Name}");
            sb.AppendLine($"Version       : {metadata.Version}");
            sb.AppendLine($"Éditeur       : {metadata.Publisher}");
            sb.AppendLine($"Développeur   : {metadata.Developer}");
            sb.AppendLine($"Catégorie     : {metadata.Category}");
            sb.AppendLine($"Sous-catégorie: {metadata.Subcategory}");
            sb.AppendLine($"Profil        : {metadata.PublicationProfile}");
            sb.AppendLine($"Système       : {metadata.OperatingSystem}");
            sb.AppendLine($"Architecture  : {metadata.Architecture}");
            sb.AppendLine($"Langue        : {metadata.Language}");
            sb.AppendLine();

            sb.AppendLine("CONFORMITÉ");
            sb.AppendLine("--------------------------------------------------");
            sb.AppendLine($"Statut affiché : {displayedStatus}");
            sb.AppendLine($"Statut moteur  : {report.Status}");
            sb.AppendLine($"Référentiel    : {report.RuleSetVersion}");
            sb.AppendLine("Export conforme autorisé : NON");
            sb.AppendLine();

            sb.AppendLine("ANOMALIES ET VÉRIFICATIONS");
            sb.AppendLine("--------------------------------------------------");

            if (report.Findings.Count == 0)
            {
                sb.AppendLine("Aucune anomalie remontée par le moteur.");
            }
            else
            {
                foreach (var finding in report.Findings)
                {
                    sb.AppendLine($"Règle      : {finding.RuleId}");
                    sb.AppendLine($"Statut     : {finding.Status}");
                    sb.AppendLine($"Description: {finding.Message}");
                    sb.AppendLine($"Cible      : {finding.Target ?? "Non précisée"}");

                    if (!string.IsNullOrWhiteSpace(finding.Correction))
                        sb.AppendLine($"Correction : {finding.Correction}");

                    sb.AppendLine();
                }
            }

            sb.AppendLine("INVENTAIRE");
            sb.AppendLine("--------------------------------------------------");

            if (inventory is null)
            {
                sb.AppendLine("Aucun inventaire disponible.");
            }
            else
            {
                sb.AppendLine($"Dossier        : {inventory.FolderPath}");
                sb.AppendLine($"Fichiers       : {inventory.FileCount}");
                sb.AppendLine($"Taille totale  : {inventory.TotalSizeBytes} octets");
                sb.AppendLine($"Inventaire complet : {(inventory.IsComplete ? "OUI" : "NON")}");
                sb.AppendLine();

                foreach (var file in inventory.Files)
                {
                    sb.AppendLine(
                        $"- {file.RelativePath} ({file.SizeBytes?.ToString() ?? "Inconnue"} octets)");
                }

                if (inventory.Warnings.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("AVERTISSEMENTS");

                    foreach (string warning in inventory.Warnings)
                        sb.AppendLine($"- {warning}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("--------------------------------------------------");
            sb.AppendLine("Ce rapport est une analyse locale indicative.");
            sb.AppendLine("Il ne garantit pas l'acceptation par la plateforme");
            sb.AppendLine("ni l'absence de fichiers malveillants.");
            sb.AppendLine("==================================================");

            File.WriteAllText(
                destinationPath,
                sb.ToString(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

}