using System;
using System.Collections.Generic;
using System.IO;

using NFOForge.App.Models;
using NFOForge.Compliance;

namespace NFOForge.App.Services
{
    public sealed class ComplianceService
    {
        private readonly ComplianceEngine _engine;
        private readonly RuleSet _rules;

        public ComplianceService()
        {
            _engine = new ComplianceEngine();

            string rulesPath = Path.Combine(
                AppContext.BaseDirectory,
                "Rules",
                "c411-applications-v1.json");

            if (!File.Exists(rulesPath))
            {
                throw new FileNotFoundException(
                    "Le référentiel C411 est introuvable.",
                    rulesPath);
            }

            _rules = RuleSetRepository.Load(rulesPath);
        }

        public ComplianceReport Analyze(
            SoftwareMetadata metadata,
            IReadOnlyList<ReleaseFile> files)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(files);

            // Les règles C411 concernent uniquement
            // la catégorie Applications / Logiciels.

            if (!string.Equals(
                    metadata.Category,
                    "Applications",
                    StringComparison.OrdinalIgnoreCase)
                ||
                !string.Equals(
                    metadata.Subcategory,
                    "Logiciels",
                    StringComparison.OrdinalIgnoreCase)
                ||
                !string.Equals(
                    metadata.PublicationProfile,
                    "C411",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new ComplianceReport(
                    ComplianceStatus.NonAnalyse,
                    Array.Empty<Finding>(),
                    false,
                    _rules.Version);
            }

            // Conversion des métadonnées de l'application
            // vers le modèle du moteur C411.

            var release = new ReleaseMetadata
            {
                Profile = metadata.PublicationProfile,

                Category = _rules.Category,

                Name = metadata.Name,

                Version = metadata.Version,

                OperatingSystem = metadata.OperatingSystem,

                Architecture = metadata.Architecture,

                Language = metadata.Language,

                Publisher = metadata.Publisher,

                Developer = metadata.Developer,

                Description = metadata.Description,

                ReleaseDate =
                    metadata.ReleaseDate?
                        .ToString("yyyy-MM-dd")
            };

            // Analyse déclarative : aucun fichier exécuté.

            return _engine.Evaluate(
                _rules,
                release,
                files);
        }
    }
}