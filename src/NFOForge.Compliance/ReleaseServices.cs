using System.Text;
using System.Text.RegularExpressions;

namespace NFOForge.Compliance;

public sealed record ClassificationProposal(string SuggestedType, bool RequiresConfirmation,
    IReadOnlyList<string> Evidence);

public static class ReleaseClassifier
{
    public static ClassificationProposal Suggest(IReadOnlyList<ReleaseFile> files)
    {
        var names = files.Select(f => Path.GetFileName(f.RelativePath)).ToArray();
        bool keygen = names.Any(x => x.Contains("keygen", StringComparison.OrdinalIgnoreCase));
        bool crack = names.Any(x => x.Contains("crack", StringComparison.OrdinalIgnoreCase));
        bool patch = names.Any(x => x.Contains("patch", StringComparison.OrdinalIgnoreCase));
        bool portable = names.Any(x => x.Contains("portable", StringComparison.OrdinalIgnoreCase));
        bool installer = names.Any(x => Regex.IsMatch(x, @"(?i)(setup|install|installer)"));
        var evidence = new List<string>();
        if (keygen) evidence.Add("Nom évoquant un keygen");
        if (crack) evidence.Add("Nom évoquant un crack");
        if (patch) evidence.Add("Nom évoquant un patch");
        if (portable) evidence.Add("Nom évoquant une version portable");
        if (installer) evidence.Add("Nom évoquant un installateur");
        var matched = new[] { keygen, crack, patch }.Count(v => v);
        string proposed = matched > 1 ? "AMBIGU" : keygen ? "KEYGEN" : crack ? "CRACK" : patch ? "PATCH" : "UNLOCKED / PREACTIVATED";
        if (!installer && (keygen || crack || patch || portable)) proposed += " + PORTABLE (à confirmer)";
        return new(proposed, true, evidence);
    }
}

public static class ReleaseNameGenerator
{
    public static string Generate(ReleaseMetadata m)
    {
        static string Require(string? v, string name) => string.IsNullOrWhiteSpace(v)
            ? throw new ArgumentException($"Champ obligatoire : {name}") : v.Trim();
        string Clean(string s) => Regex.Replace(s.Trim(), @"\s+", " ");
        var name = Clean(Require(m.Name, "Nom"));
        var version = Clean(Require(m.Version, "Version"));
        var os = Clean(Require(m.OperatingSystem, "Plateforme"));
        var language = Clean(Require(m.Language, "Langue"));
        var release = Clean(Require(m.ReleaseType, "Type de release confirmé"));
        var arch = string.IsNullOrWhiteSpace(m.Architecture) ? "" : " " + Clean(m.Architecture);
        var team = string.IsNullOrWhiteSpace(m.Team) ? "" : " -" + Clean(m.Team);
        var parent = m.IsPlugin ? " [" + Clean(Require(m.ParentSoftware, "Logiciel parent")) + "]" : "";
        var output = $"{name} {version}{team}{parent} [{os}{arch} {language} {release}]";
        if (output.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Le nom contient un caractère interdit par Windows.");
        return output;
    }
}

public static class NfoGenerator
{
    public static string Generate(ReleaseMetadata m, int fileCount)
    {
        var s = new StringBuilder();
        void Add(string title, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) s.AppendLine($"{title} : {value.Trim()}");
        }
        Add("Application", m.Name);
        Add("Version", m.Version);
        Add("Plateforme", m.OperatingSystem);
        Add("Éditeur", m.Publisher);
        Add("Développeur", m.Developer);
        Add("Genre", m.Genre);
        Add("Date de sortie", m.ReleaseDate);
        Add("Région", m.Region);
        Add("Langue", m.Language);
        Add("Format", m.Format);
        Add("Nombre de fichiers", fileCount >= 0 ? fileCount.ToString() : null);
        Add("Description", m.Description);
        Add("Configuration requise", m.Requirements);
        Add("Consignes d'installation", m.InstallationNotes);
        return s.ToString().TrimEnd() + Environment.NewLine;
    }
}

public static class BbcodeValidator
{
    public static IReadOnlyList<string> Missing(ReleaseMetadata m)
    {
        var issues = new List<string>();
        if (!IsHttpImageUrl(m.LogoUrl)) issues.Add("Logo manquant ou URL invalide");
        if (string.IsNullOrWhiteSpace(m.Description)) issues.Add("Description manquante");
        if (m.Screenshots.Count(x => IsHttpImageUrl(x)) < (m.SingleScreenApplication ? 1 : 2))
            issues.Add("Captures d'écran insuffisantes");
        if (string.IsNullOrWhiteSpace(m.Requirements)) issues.Add("Configuration manquante");
        if (string.IsNullOrWhiteSpace(m.InstallationNotes)) issues.Add("Notes d'installation manquantes");
        if (string.IsNullOrWhiteSpace(m.DownloadInfo)) issues.Add("Informations de téléchargement manquantes");
        return issues;
    }
    public static int CompletionScore(ReleaseMetadata m) =>
        (int)Math.Round(100d * (6 - Missing(m).Count) / 6, MidpointRounding.AwayFromZero);
    public static bool IsHttpImageUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && new[] { ".png", ".jpg", ".jpeg", ".webp" }
             .Any(ext => uri.AbsolutePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
}
