using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NFOForge.App.Models;

namespace NFOForge.App.Services
{
    public sealed class BBCodeTemplateService
    {
        private const int MaxTemplateCharacters = 100_000;
        private const int MaxVariables = 100;

        private static readonly Regex VariablePattern = new(
            @"\{\{(?<double>[A-Za-z][A-Za-z0-9_]*)\}\}|\{(?<single>[A-Za-z][A-Za-z0-9_]*)\}",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        private static readonly Dictionary<string, string> Aliases =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["TITLE"] = "Nom",
                ["OVERVIEW"] = "Description",
                ["PUBLISHER"] = "Editeur",
                ["DEVELOPER"] = "Developpeur",
                ["CATEGORY"] = "Categorie",
                ["SUBCATEGORY"] = "SousCategorie",
                ["OS"] = "Systeme",
                ["LANGUAGE"] = "Langue",
                ["FEATURES"] = "Fonctionnalites",
                ["ADDITIONAL_INFO"] = "Informations",
                ["RELEASE_DATE"] = "DateSortie",
                ["POSTER_URL"] = "PosterUrl",
                ["SCREENSHOT_URL"] = "ScreenshotUrl"
            };

        private static readonly string[] Variables =
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
            "DateSortie",
            "PosterUrl",
            "ScreenshotUrl"
        ];

        public IReadOnlyList<string> GetAvailableVariables()
        {
            return Array.AsReadOnly(Variables);
        }

        private static string ResolveVariable(string name)
        {
            return Aliases.TryGetValue(
                name,
                out string? canonical)
                ? canonical
                : name;
        }

        private static string GetVariableName(Match match)
        {
            return match.Groups["double"].Success
                ? match.Groups["double"].Value
                : match.Groups["single"].Value;
        }

        public IReadOnlyList<string> GetCustomVariables(
            string? content)
        {
            ValidateTemplateLength(content);

            var automatic = new HashSet<string>(
                Variables,
                StringComparer.OrdinalIgnoreCase);

            var found = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            var result = new List<string>();

            foreach (Match match in VariablePattern.Matches(
                content ?? string.Empty))
            {
                string name = GetVariableName(match);
                string canonical = ResolveVariable(name);

                if (automatic.Contains(canonical))
                    continue;

                if (found.Add(canonical))
                    result.Add(canonical);

                if (result.Count > MaxVariables)
                {
                    throw new ArgumentException(
                        "Le modèle contient trop de variables personnalisées.");
                }
            }

            return result.AsReadOnly();
        }

        public void ValidateVariables(string? content)
        {
            ValidateTemplateLength(content);

            // Les variables reconnues par la syntaxe
            // sont acceptées comme paramètres personnalisables.
            // Leur valeur sera vérifiée à la génération.
            _ = GetCustomVariables(content);
        }

        private static void ValidateTemplateLength(string? content)
        {
            if ((content?.Length ?? 0) > MaxTemplateCharacters)
            {
                throw new ArgumentException(
                    "Le modèle BBCode dépasse la taille autorisée.");
            }
        }

        public string Render(
            BBCodeTemplate template,
            SoftwareMetadata metadata,
            string? posterUrl = null,
            string? screenshotUrl = null,
            IReadOnlyDictionary<string, string>? customValues = null)
        {
            ArgumentNullException.ThrowIfNull(template);
            ArgumentNullException.ThrowIfNull(metadata);

            ValidateVariables(template.Content);

            var values = new Dictionary<string, string>(
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
                    ?? string.Empty,

                ["PosterUrl"] = ValidateUrl(posterUrl),
                ["ScreenshotUrl"] = ValidateUrl(screenshotUrl)
            };

            var automatic = new HashSet<string>(
                Variables,
                StringComparer.OrdinalIgnoreCase);

            if (customValues is not null)
            {
                foreach (var item in customValues)
                {
                    string canonical = ResolveVariable(item.Key);

                    // Les champs automatiques ne peuvent pas
                    // être remplacés par des valeurs personnalisées.
                    if (automatic.Contains(canonical))
                        continue;

                    values[canonical] = item.Value ?? string.Empty;
                }
            }

            return VariablePattern.Replace(
                template.Content ?? string.Empty,
                match =>
                {
                    string originalName = GetVariableName(match);
                    string canonical = ResolveVariable(originalName);

                    if (!values.TryGetValue(
                        canonical,
                        out string? value) ||
                        string.IsNullOrWhiteSpace(value))
                    {
                        throw new ArgumentException(
                            "Variable BBCode non renseignée : " +
                            originalName);
                    }

                    if (IsUrlVariable(canonical))
                        value = ValidateUrl(value);

                    return value;
                });
        }

        private static bool IsUrlVariable(string name)
        {
            return name.Equals(
                       "PosterUrl",
                       StringComparison.OrdinalIgnoreCase)
                   ||
                   name.Equals(
                       "ScreenshotUrl",
                       StringComparison.OrdinalIgnoreCase)
                   ||
                   name.EndsWith(
                       "_URL",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string ValidateUrl(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string url = value.Trim();

            if (url.Length > 2048 ||
                !Uri.TryCreate(
                    url,
                    UriKind.Absolute,
                    out Uri? uri) ||
                (uri.Scheme != Uri.UriSchemeHttp &&
                 uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException(
                    "L'URL doit être une adresse HTTP ou HTTPS valide.");
            }

            return url;
        }
    }
}