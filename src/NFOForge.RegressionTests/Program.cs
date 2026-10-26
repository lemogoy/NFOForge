using System.IO;
using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;
using NFOForge.App.Models;
using NFOForge.App.Services;
using NFOForge.App.ViewModels;
using NFOForge.Compliance;

// Tests sans paquet externe ; une assertion en échec retourne un code non nul.
internal static class Program
{
    private static int _checks;

    [STAThread]
    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "NFOForge-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            Run(root);
            Console.WriteLine($"SUCCÈS : {_checks} assertions.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        // Les petits résultats sont conservés pour inspection, sans suppression récursive.
    }

    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Verify(ComplianceViewModel vm)
    {
        vm.VerifyCommand.Execute(null);
        Check(vm.ExportReportCommand.CanExecute(null), "Rapport frais non exportable : " + vm.Details);
        Check(!vm.AllowConformingExport, "La certification doit rester désactivée.");
    }

    private static void Run(string root)


    {
        // Régression du gestionnaire de mises à jour.
        UpdateServiceTests.Run(Check);
        UpdatePreferencesTests.Run(Check, root);
        UpdateNotificationTests.Run(Check);

        var metadata = new SoftwareMetadata { Name = "Test", Version = "1.0" };
        string? folder = null;
        int dialogs = 0;
        Action? duringSave = null;
        int format = 1;
        bool accept = true;
        string destination = Path.Combine(root, "rapport");
        bool? Save(SaveFileDialog dialog)
        {
            dialogs++;
            dialog.FileName = destination;
            dialog.FilterIndex = format;
            duringSave?.Invoke();
            return accept;
        }
        var vm = new ComplianceViewModel(metadata, Save, () => folder);
        Check(!vm.ExportReportCommand.CanExecute(null), "Export sans analyse.");
        vm.ExportReportCommand.Execute(null);
        Check(dialogs == 0, "Appel direct sans rapport : dialogue ouvert.");

        // Toutes les propriétés, y compris celles absentes des contrôles C411.
        foreach (PropertyInfo property in typeof(SoftwareMetadata).GetProperties())
        {
            Verify(vm);
            int changes = 0;
            EventHandler handler = (_, _) => changes++;
            vm.ExportReportCommand.CanExecuteChanged += handler;
            object? old = property.GetValue(metadata);
            property.SetValue(metadata, old);
            Check(vm.ExportReportCommand.CanExecute(null), "Même valeur invalidée : " + property.Name);
            object value = property.PropertyType == typeof(string)
                ? (old as string) + " modifié"
                : new DateTime(2026, 10, 18);
            property.SetValue(metadata, value);
            Check(!vm.ExportReportCommand.CanExecute(null), "Modification ignorée : " + property.Name);
            Check(changes > 0 && vm.Status == "NON ANALYSÉ" && vm.Findings.Count == 0,
                "Interface non invalidée : " + property.Name);
            int before = dialogs;
            vm.ExportReportCommand.Execute(null);
            Check(dialogs == before, "Appel direct périmé : dialogue ouvert.");
            property.SetValue(metadata, old);
            Check(!vm.ExportReportCommand.CanExecute(null), "Retour à l'ancienne valeur sans vérification.");
            vm.ExportReportCommand.CanExecuteChanged -= handler;
        }

        // Même chemin rescanné, annulation et erreur d'inventaire.
        string input = Path.Combine(root, "inventaire");
        Directory.CreateDirectory(input);
        File.WriteAllText(Path.Combine(input, "readme.txt"), "test");
        folder = input;
        vm.BrowseFolderCommand.Execute(null);
        Verify(vm);
        folder = null;
        vm.BrowseFolderCommand.Execute(null);
        Check(vm.ExportReportCommand.CanExecute(null), "Annulation du choix de dossier invalidante.");
        folder = input;
        vm.BrowseFolderCommand.Execute(null);
        Check(!vm.ExportReportCommand.CanExecute(null), "Rescan du même chemin non invalidant.");
        Verify(vm);
        folder = Path.Combine(root, "absent");
        vm.BrowseFolderCommand.Execute(null);
        Check(!vm.ExportReportCommand.CanExecute(null), "Inventaire en échec exportable.");

        // Protection des listes d'inventaire si elles sont modifiées par un futur appelant.
        var files = new List<ReleaseFile> { new("readme.txt", 4) };
        var warnings = new List<string>();
        var inventory = new ReleaseInventoryResult(input, files, 4, warnings, 0, 0);
        typeof(ComplianceViewModel).GetField("_inventory", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(vm, inventory);
        Verify(vm);
        files.Add(new ReleaseFile("nouveau.txt", 1));
        Check(!vm.ExportReportCommand.CanExecute(null), "Liste de fichiers modifiée non détectée.");
        int previous = dialogs;
        vm.ExportReportCommand.Execute(null);
        Check(dialogs == previous, "Inventaire périmé accepté par Execute.");
        Verify(vm);
        warnings.Add("Inventaire incomplet");
        Check(!vm.ExportReportCommand.CanExecute(null), "Avertissement modifié non détecté.");

        foreach (int filter in new[] { 1, 2 })
        {
            format = filter;
            string extension = filter == 1 ? ".json" : ".txt";
            destination = Path.Combine(root, "perime-" + filter);
            Verify(vm);
            duringSave = () => metadata.Version += " changé";
            vm.ExportReportCommand.Execute(null);
            Check(!File.Exists(destination + extension), "Export périmé pendant le dialogue : " + extension);
            Check(!vm.ExportReportCommand.CanExecute(null), "Export périmé toujours activé.");

            Verify(vm);
            duringSave = () => files.Add(new ReleaseFile("ajout-" + filter, 2));
            vm.ExportReportCommand.Execute(null);
            Check(!File.Exists(destination + extension), "Inventaire changé pendant le dialogue : " + extension);

            duringSave = null;
            Verify(vm);
            accept = false;
            vm.ExportReportCommand.Execute(null);
            Check(!File.Exists(destination + extension) && vm.ExportReportCommand.CanExecute(null),
                "Annulation d'export incorrecte.");
            accept = true;
            vm.ExportReportCommand.Execute(null);
            Check(File.Exists(destination + extension), "Export frais absent : " + extension);
            string content = File.ReadAllText(destination + extension);
            Check(content.Contains("NFOForge", StringComparison.OrdinalIgnoreCase), "Rapport vide.");
            if (filter == 1)
            {
                using var json = JsonDocument.Parse(content);
                Check(json.RootElement.GetProperty("Software").GetProperty("Version").GetString() == metadata.Version,
                    "Mauvaise version dans le JSON.");
            }
        }

        // Passage par le ViewModel utilisé par les champs WPF.
        var editor = new MetadataViewModel(metadata);
        Verify(vm);
        editor.Name = "Nom saisi dans l'éditeur";
        Check(!vm.ExportReportCommand.CanExecute(null), "Modification via MetadataViewModel ignorée.");
        Verify(vm);
        editor.ReleaseDate = new DateTime(2025, 1, 1);
        Check(!vm.ExportReportCommand.CanExecute(null), "Date via MetadataViewModel ignorée.");

        // Une erreur après le calcul, pendant l'affichage, ne publie aucun rapport.
        bool failOnce = true;
        System.ComponentModel.PropertyChangedEventHandler failDisplay = (_, e) =>
        {
            if (e.PropertyName == nameof(vm.RuleVersion) && vm.RuleVersion != "Non chargée" && failOnce)
            {
                failOnce = false;
                throw new InvalidOperationException("Erreur d'affichage simulée");
            }
        };
        vm.PropertyChanged += failDisplay;
        vm.VerifyCommand.Execute(null);
        Check(vm.Status == "ERREUR D'ANALYSE" && !vm.ExportReportCommand.CanExecute(null),
            "Rapport publié malgré une erreur après le calcul.");
        vm.PropertyChanged -= failDisplay;

        // Échec d'une nouvelle analyse après un rapport valide.
        Verify(vm);
        string rules = Path.Combine(AppContext.BaseDirectory, "Rules", "c411-applications-v1.json");
        string original = File.ReadAllText(rules);
        try
        {
            File.WriteAllText(rules, "{ invalide");
            vm.VerifyCommand.Execute(null);
            Check(vm.Status == "ERREUR D'ANALYSE" && !vm.ExportReportCommand.CanExecute(null),
                "Rapport précédent conservé après échec d'analyse.");
        }
        finally { File.WriteAllText(rules, original); }
        Verify(vm);
    }
}
