using System;

namespace NFOForge.App.Services
{
    /// <summary>
    /// Normalise les fins de ligne des exports NFO au format Windows CRLF.
    /// Ne modifie ni les caractères du contenu, ni les lignes vides,
    /// ni la présence d'un saut de ligne final.
    /// </summary>
    public static class NfoLineEndingNormalizer
    {
        /// <summary>
        /// Convertit CRLF, LF et CR en CRLF sans ajouter de ligne supplémentaire.
        /// La méthode est idempotente et indépendante de l'encodage choisi.
        /// </summary>
        public static string NormalizeToCrLf(string content)
        {
            ArgumentNullException.ThrowIfNull(content);

            return content
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Replace("\n", "\r\n", StringComparison.Ordinal);
        }
    }
}
