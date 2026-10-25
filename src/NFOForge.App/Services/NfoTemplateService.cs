
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NFOForge.App.Models;

namespace NFOForge.App.Services
{
    public sealed class NfoTemplateService
    {
        private static readonly Regex VariablePattern = new(
            @"\{([A-Za-z][A-Za-z0-9]*)\}",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        public string Render(
            NfoTemplate template,
            SoftwareMetadata metadata)
        {
            ArgumentNullException.ThrowIfNull(template);
            ArgumentNullException.ThrowIfNull(metadata);

            var variables = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["Nom"] = metadata.Name ?? string.Empty,
                ["Version"] = metadata.Version ?? string.Empty,
                ["Editeur"] = metadata.Publisher ?? string.Empty,
                ["Developpeur"] = metadata.Developer ?? string.Empty,
                ["Categorie"] = metadata.Category ?? string.Empty,
                ["SousCategorie"] = metadata.Subcategory ?? string.Empty,
                ["Systeme"] = metadata.OperatingSystem ?? string.Empty,
                ["Architecture"] = metadata.Architecture ?? string.Empty,
                ["Langue"] = metadata.Language ?? string.Empty,
                ["Description"] = metadata.Description ?? string.Empty,
                ["Fonctionnalites"] = metadata.Features ?? string.Empty,
                ["Informations"] =
                    metadata.AdditionalInformation ?? string.Empty,
                ["DateSortie"] =
                    metadata.ReleaseDate?.ToString("dd/MM/yyyy")
                    ?? string.Empty
            };

            return VariablePattern.Replace(
                template.Content ?? string.Empty,
                match =>
                {
                    string name = match.Groups[1].Value;

                    if (variables.TryGetValue(name, out string? value))
                        return value;

                    throw new ArgumentException(
                        $"Variable NFO inconnue : {name}");
                });
        }

        public IReadOnlyList<string> GetAvailableVariables()
        {
            return
            [
                "Nom",
                "Version",
                "Editeur",
                "Developpeur",
                "Categorie",
                "SousCategorie",
                "Systeme",
                "Architecture",
                "Langue",
                "Description",
                "Fonctionnalites",
                "Informations",
                "DateSortie"
            ];
        }
    }
}
