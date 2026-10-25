namespace NFOForge.Compliance;

public enum ComplianceStatus { Conforme, AVerifier, NonConforme, NonAnalyse }
public enum RuleSeverity { Information, Avertissement, Bloquant }

public sealed record ReleaseFile(string RelativePath, long? SizeBytes = null);
public sealed record RuleDefinition(string Id, string Category, string Profile,
    string Description, string Condition, RuleSeverity Severity, string Explanation,
    string? Correction, bool Enabled, string ReferenceVersion);
public sealed record RuleSet(string Version, string Profile, string Category, List<RuleDefinition> Rules);
public sealed record Finding(string RuleId, ComplianceStatus Status, RuleSeverity Severity,
    string Message, string? Target = null, string? Correction = null);
public sealed record ComplianceReport(ComplianceStatus Status, IReadOnlyList<Finding> Findings,
    bool AllowConformingExport, string RuleSetVersion);
public sealed class ReleaseMetadata
{
    public string Profile { get; set; } = "C411";
    public string Category { get; set; } = "Applications / Logiciels";
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? OperatingSystem { get; set; }
    public string? Language { get; set; }
    public string? Architecture { get; set; }
    public string? Team { get; set; }
    public string? ReleaseType { get; set; }
    public string? ParentSoftware { get; set; }
    public bool IsPlugin { get; set; }
    public bool? IsAlphaOrEarlyAccess { get; set; }
    public bool? IsSourceOrDecompiled { get; set; }
    public bool? IsLinuxException { get; set; }
    public bool? IsOlderSameGeneration { get; set; }
    public bool? IsHypervisorCrackGame { get; set; }
    public bool? IsDownloader { get; set; }
    public bool? IsPaidWithoutRequiredActivation { get; set; }
    public bool? IsTrainerOrHackTool { get; set; }
    public bool? IsIptv { get; set; }
    public bool? MalwareDetected { get; set; }
    public bool? ZipIsSupportOnly { get; set; }
    public bool? IsoProvenanceVerifiedAndAllowed { get; set; }
    public string? Description { get; set; }
    public string? Publisher { get; set; }
    public string? Developer { get; set; }
    public string? Genre { get; set; }
    public string? ReleaseDate { get; set; }
    public string? Region { get; set; }
    public string? Format { get; set; }
    public string? Requirements { get; set; }
    public string? InstallationNotes { get; set; }
    public string? LogoUrl { get; set; }
    public List<string> Screenshots { get; set; } = [];
    public bool SingleScreenApplication { get; set; }
    public string? DownloadInfo { get; set; }
}
