using NFOForge.Compliance;

var path = Path.Combine(AppContext.BaseDirectory, "rules", "c411-applications-v1.json");
var rules = RuleSetRepository.Load(path);
int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("ÉCHEC : " + name);
    Console.WriteLine("OK : " + name);
    passed++;
}
var engine = new ComplianceEngine();
var defaults = new ReleaseMetadata();
Check(engine.Evaluate(rules, defaults, [new("test.7z")]).Status == ComplianceStatus.NonConforme, "7z interdit");
Check(engine.Evaluate(rules, defaults, [new("test.rar")]).Status == ComplianceStatus.NonConforme, "rar interdit");
Check(engine.Evaluate(rules, defaults, [new("support.zip")]).Findings.Any(x => x.RuleId == "C411-FMT-002" && x.Status == ComplianceStatus.AVerifier), "zip ambigu");
defaults.ZipIsSupportOnly = true;
Check(!engine.Evaluate(rules, defaults, [new("support.zip")]).Findings.Any(x => x.RuleId == "C411-FMT-002"), "exception zip déclarée");
defaults.IsAlphaOrEarlyAccess = true;
Check(engine.Evaluate(rules, defaults, []).Findings.Any(x => x.RuleId == "C411-VER-001" && x.Status == ComplianceStatus.NonConforme), "alpha refusée");
Check(ReleaseClassifier.Suggest([new("Keygen.exe"), new("setup.exe")]).SuggestedType == "KEYGEN", "classification keygen");
Check(ReleaseClassifier.Suggest([new("crack.dll"), new("patch.exe")]).SuggestedType == "AMBIGU", "classification ambiguë");
Check(ReleaseClassifier.Suggest([]).RequiresConfirmation, "confirmation classification systématique");
var m = new ReleaseMetadata { Name="Exemple.Application", Version="v2.5", OperatingSystem="Windows", Language="FR", ReleaseType="PORTABLE" };
Check(ReleaseNameGenerator.Generate(m) == "Exemple.Application v2.5 [Windows FR PORTABLE]", "nommage application");
m.IsPlugin = true; m.ParentSoftware = "LogicielParent"; m.Architecture="x64";
Check(ReleaseNameGenerator.Generate(m).Contains("[LogicielParent] [Windows x64 FR PORTABLE]"), "nommage plugin");
Check(BbcodeValidator.Missing(m).Count == 6, "BBCode champs manquants");
m.Description = "Description"; m.LogoUrl="https://example.com/logo.webp";
m.Requirements="Windows"; m.InstallationNotes="Installer depuis la source officielle";
m.DownloadInfo="Fichier / taille / format";
m.Screenshots.AddRange(["https://example.com/1.png", "https://example.com/2.png"]);
Check(BbcodeValidator.CompletionScore(m) == 100, "BBCode complet");
var nfo = NfoGenerator.Generate(m, 2);
Check(nfo.IndexOf("Application", StringComparison.Ordinal) < nfo.IndexOf("Version", StringComparison.Ordinal) &&
      nfo.IndexOf("Version", StringComparison.Ordinal) < nfo.IndexOf("Plateforme", StringComparison.Ordinal) &&
      nfo.IndexOf("Description", StringComparison.Ordinal) < nfo.IndexOf("Configuration requise", StringComparison.Ordinal), "ordre NFO");
Check(!nfo.Contains("Éditeur :"), "pas de données inventées");
var other = new ReleaseMetadata { Category = "Films" };
Check(engine.Evaluate(rules, other, []).Status == ComplianceStatus.NonAnalyse, "isolation des catégories");
Console.WriteLine($"{passed} tests validés.");
