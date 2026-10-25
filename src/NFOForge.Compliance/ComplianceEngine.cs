namespace NFOForge.Compliance;

/// <summary>Analyse déclarative, sans ouverture ni exécution de contenu binaire.</summary>
public sealed class ComplianceEngine
{
    public ComplianceReport Evaluate(RuleSet rules, ReleaseMetadata metadata, IReadOnlyList<ReleaseFile> files)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(files);
        if (!string.Equals(rules.Profile, metadata.Profile, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(rules.Category, metadata.Category, StringComparison.OrdinalIgnoreCase))
            return new(ComplianceStatus.NonAnalyse, [], false, rules.Version);

        var findings = new List<Finding>();
        foreach (var rule in rules.Rules.Where(r => r.Enabled &&
                 string.Equals(r.Profile, metadata.Profile, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(r.Category, metadata.Category, StringComparison.OrdinalIgnoreCase)))
        {
            var (status, target) = Test(rule.Condition, metadata, files);
            if (status == ComplianceStatus.Conforme) continue;
            findings.Add(new Finding(rule.Id, status, rule.Severity,
                rule.Explanation, target, rule.Correction));
        }
        var result = findings.Any(f => f.Status == ComplianceStatus.NonConforme)
            ? ComplianceStatus.NonConforme
            : findings.Any(f => f.Status == ComplianceStatus.AVerifier)
                ? ComplianceStatus.AVerifier
                : findings.Any(f => f.Status == ComplianceStatus.NonAnalyse)
                    ? ComplianceStatus.NonAnalyse : ComplianceStatus.Conforme;
        // L'export dit « conforme » exige une évaluation concluante et l'absence d'alertes.
        return new(result, findings, result == ComplianceStatus.Conforme, rules.Version);
    }

    private static (ComplianceStatus, string?) Test(string condition, ReleaseMetadata m, IReadOnlyList<ReleaseFile> files)
    {
        var suffix = condition switch
        {
            "archive_rar_7z" => new[] { ".rar", ".7z" },
            "archive_zip" => new[] { ".zip" },
            "iso" => new[] { ".iso" },
            _ => []
        };
        if (suffix.Length != 0)
        {
            var file = files.FirstOrDefault(f => suffix.Any(e => f.RelativePath.EndsWith(e, StringComparison.OrdinalIgnoreCase)));
            if (file is null) return (ComplianceStatus.Conforme, null);
            return condition switch
            {
                "archive_rar_7z" => (ComplianceStatus.NonConforme, file.RelativePath),
                "archive_zip" => (m.ZipIsSupportOnly == true ? ComplianceStatus.Conforme
                    : m.ZipIsSupportOnly == false ? ComplianceStatus.NonConforme : ComplianceStatus.AVerifier, file.RelativePath),
                "iso" => (m.IsoProvenanceVerifiedAndAllowed == true ? ComplianceStatus.Conforme
                    : m.IsoProvenanceVerifiedAndAllowed == false ? ComplianceStatus.NonConforme : ComplianceStatus.AVerifier, file.RelativePath),
                _ => (ComplianceStatus.NonAnalyse, file.RelativePath)
            };
        }
        bool? flag = condition switch
        {
            "alpha_early_access" => m.IsAlphaOrEarlyAccess,
            "source_decompiled" => m.IsSourceOrDecompiled == true && m.IsLinuxException == true ? false : m.IsSourceOrDecompiled,
            "older_generation" => m.IsOlderSameGeneration,
            "hypervisor_crack" => m.IsHypervisorCrackGame,
            "downloader" => m.IsDownloader,
            "paid_unusable" => m.IsPaidWithoutRequiredActivation,
            "hack_tool" => m.IsTrainerOrHackTool,
            "iptv" => m.IsIptv,
            "malware" => m.MalwareDetected,
            _ => null
        };
        return flag switch
        {
            true => (ComplianceStatus.NonConforme, null),
            false => (ComplianceStatus.Conforme, null),
            null => (ComplianceStatus.NonAnalyse, null)
        };
    }
}
