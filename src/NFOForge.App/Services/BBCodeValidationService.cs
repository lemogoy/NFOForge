using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NFOForge.App.Services
{
    /// <summary>Contrôle structurel local du sous-ensemble BBCode pris en charge par le rendu.</summary>
    public sealed class BBCodeValidationService
    {
        private const int MaxCharacters = 100_000;
        private const int MaxDepth = 32;

        private static readonly Regex TagPattern = new(
            @"\[(/?)([a-zA-Z]+|\*)(?:=([^\]\r\n]*))?\]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        private static readonly Regex HexColorPattern = new(
            @"^#[0-9a-fA-F]{6}$", RegexOptions.Compiled | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        private static readonly HashSet<string> SupportedTags = new(StringComparer.OrdinalIgnoreCase)
        {
            "b", "i", "u", "color", "size", "center", "url", "img", "quote", "list"
        };

        public string Validate(string? bbcode)
        {
            string source = bbcode ?? string.Empty;
            if (source.Length > MaxCharacters)
                return $"Validation limitée à {MaxCharacters:N0} caractères.";

            var stack = new Stack<string>();
            int position = 0;

            foreach (Match match in TagPattern.Matches(source))
            {
                // La validation n'interprète pas de HTML ou de script ; les balises
                // BBCode reconnues sont traitées comme de simples marqueurs.
                string name = match.Groups[2].Value.ToLowerInvariant();
                bool closing = match.Groups[1].Value == "/";
                bool hasParameter = match.Groups[3].Success;
                string parameter = match.Groups[3].Value;
                position = match.Index + match.Length;

                if (name == "*")
                {
                    if (closing || hasParameter || stack.Count == 0 || stack.Peek() != "list")
                        return $"Marqueur [*] incorrect à la position {match.Index + 1}.";
                    continue;
                }

                if (!SupportedTags.Contains(name))
                    return $"Balise non prise en charge : [{name}].";

                if (closing)
                {
                    if (hasParameter || stack.Count == 0 || stack.Peek() != name)
                        return $"Fermeture incorrecte : {match.Value} (position {match.Index + 1}).";
                    stack.Pop();
                    continue;
                }

                if (stack.Count >= MaxDepth)
                    return $"Imbrication supérieure à {MaxDepth} niveaux.";

                if (name == "color" && (!hasParameter || !HexColorPattern.IsMatch(parameter)))
                    return "Paramètre [color] invalide : utiliser #RRGGBB.";

                if (name == "size" && (!hasParameter ||
                    !int.TryParse(parameter, NumberStyles.None, CultureInfo.InvariantCulture, out int size) ||
                    size < 8 || size > 48))
                    return "Paramètre [size] invalide : utiliser un entier de 8 à 48.";

                if (name == "url" && hasParameter && !IsHttpUrl(parameter))
                    return "Paramètre [url] invalide : seules les URL HTTP et HTTPS sont admises.";

                if (hasParameter && name is not ("color" or "size" or "url"))
                    return $"La balise [{name}] n'accepte pas de paramètre.";

                stack.Push(name);
            }

            if (stack.Count > 0)
                return $"Balise non fermée : [{stack.Peek()}].";

            return "Structure BBCode reconnue valide (contrôle partiel).";
        }

        private static bool IsHttpUrl(string value) =>
            Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
            !string.IsNullOrWhiteSpace(uri.Host);
    }
}
