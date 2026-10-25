
using System;
using System.Collections.Generic;
using NFOForge.App.Models;

namespace NFOForge.App.Services
{
    public sealed class MetadataValidationService
    {
        public IReadOnlyList<string> Validate(SoftwareMetadata metadata)
        {
            ArgumentNullException.ThrowIfNull(metadata);

            var errors = new List<string>();

            // 1. Identification
            if (string.IsNullOrWhiteSpace(metadata.Name))
                errors.Add("Le nom du logiciel est obligatoire.");

            if (string.IsNullOrWhiteSpace(metadata.Version))
                errors.Add("La version est obligatoire.");

            // 2. Classification
            if (string.IsNullOrWhiteSpace(metadata.Category))
            {
                errors.Add("La catégorie doit être renseignée.");
            }
            else if (string.Equals(
                metadata.Category,
                "Applications",
                StringComparison.OrdinalIgnoreCase))
            {
                // Le profil C411 n'est actuellement disponible
                // que pour Applications / Logiciels.

                if (!string.Equals(
                    metadata.Subcategory,
                    "Logiciels",
                    StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(
                        "La sous-catégorie Logiciels est obligatoire " +
                        "pour les applications.");
                }

                if (!string.Equals(
                    metadata.PublicationProfile,
                    "C411",
                    StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(
                        "Le profil C411 doit être sélectionné " +
                        "pour Applications / Logiciels.");
                }
            }
            else
            {
                // Catégories sans référentiel disponible.
                // Une absence de profil est normale.
                // En revanche, une ancienne sélection C411
                // ne doit pas rester associée à ces catégories.

                if (string.Equals(
                    metadata.PublicationProfile,
                    "C411",
                    StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(
                        "Le profil C411 est réservé à " +
                        "Applications / Logiciels.");
                }
            }

            // 3. Informations techniques
            if (string.IsNullOrWhiteSpace(metadata.OperatingSystem))
                errors.Add("Le système d'exploitation est obligatoire.");

            if (string.IsNullOrWhiteSpace(metadata.Architecture))
                errors.Add("L'architecture doit être renseignée.");

            if (string.IsNullOrWhiteSpace(metadata.Language))
                errors.Add("La langue est obligatoire.");

            // 4. Présentation
            if (string.IsNullOrWhiteSpace(metadata.Description))
                errors.Add(
                    "Une description du logiciel est obligatoire.");

            return errors;
        }
    }
}
