using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using NFOForge.App.Commands;
using NFOForge.App.Models;
using NFOForge.App.Services;
using NFOForge.Compliance;

namespace NFOForge.App.ViewModels
{
    public sealed class ComplianceViewModel : INotifyPropertyChanged
    {
        private readonly SoftwareMetadata _metadata;
        // Service d'exportation des rapports
        private readonly ComplianceReportExportService _exportService = new();

        // Dernier rapport généré par le moteur C411
        private ComplianceReport? _lastReport;
        private string? _analyzedInputs;
        private readonly Func<SaveFileDialog, bool?> _showSaveDialog;
        private readonly Func<string?> _selectFolder;

        // Commande d'exportation
        public ICommand ExportReportCommand { get; }

        private readonly ReleaseInventoryService _inventoryService = new();

        private ReleaseInventoryResult? _inventory;

        private string _selectedFolder = "Aucun dossier sélectionné";

        public string SelectedFolder
        {
            get => _selectedFolder;
            private set
            {
                _selectedFolder = value;
                OnPropertyChanged();
            }
        }

        private string _inventorySummary = "Aucun inventaire disponible";

        public string InventorySummary
        {
            get => _inventorySummary;
            private set
            {
                _inventorySummary = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<string> InventoryFiles { get; } = new();

        public ObservableCollection<string> InventoryWarnings { get; } = new();

        public ICommand BrowseFolderCommand { get; }

        private string _status = "NON ANALYSÉ";

        private string _details =
            "Cliquez sur Vérifier pour lancer le contrôle.";

        private string _ruleVersion = "Non chargée";

        private bool _allowConformingExport;

        public ObservableCollection<string> Findings { get; } = new();

        public string Status
        {
            get => _status;
            private set
            {
                _status = value;
                OnPropertyChanged();
            }
        }

        public string Details
        {
            get => _details;
            private set
            {
                _details = value;
                OnPropertyChanged();
            }
        }

        public string RuleVersion
        {
            get => _ruleVersion;
            private set
            {
                _ruleVersion = value;
                OnPropertyChanged();
            }
        }

        public bool AllowConformingExport
        {
            get => _allowConformingExport;
            private set
            {
                _allowConformingExport = value;
                OnPropertyChanged();
            }
        }

        public ICommand VerifyCommand { get; }

        public ComplianceViewModel(SoftwareMetadata metadata)
            : this(metadata, dialog => dialog.ShowDialog(),
                () => new FolderPickerService().SelectFolder())
        {
        }

        // Substitution des dialogues dans les tests sans modifier l'API publique.
        internal ComplianceViewModel(SoftwareMetadata metadata,
            Func<SaveFileDialog, bool?> showSaveDialog, Func<string?> selectFolder)
        {
            _showSaveDialog = showSaveDialog;
            _selectFolder = selectFolder;
            _metadata = metadata
                ?? throw new ArgumentNullException(nameof(metadata));

            VerifyCommand = new RelayCommand(_ => Verify());

            BrowseFolderCommand = new RelayCommand(_ => BrowseFolder());

            ExportReportCommand = new RelayCommand(
                _ => ExportReport(),
                _ => IsReportCurrent());

            _metadata.PropertyChanged += (_, _) => InvalidateReport(
                "Métadonnées modifiées. Relancez la vérification.");
        }
        private void BrowseFolder()
        {
            string? folder;

            try
            {
                folder = _selectFolder();

                if (string.IsNullOrWhiteSpace(folder))
                    return;

                InvalidateReport("Inventaire modifié. Relancez la vérification.");
                _inventory = null;
                InventoryFiles.Clear();
                InventoryWarnings.Clear();
                SelectedFolder = folder;
                InventorySummary = "Analyse du dossier en cours...";

                var result = _inventoryService.Scan(folder);

                _inventory = result;

                foreach (var file in result.Files)
                {
                    InventoryFiles.Add(
                        $"{file.RelativePath} — {file.SizeBytes:N0} octets");
                }

                foreach (string warning in result.Warnings)
                {
                    InventoryWarnings.Add(warning);
                }

                InventorySummary =
                    $"{result.FileCount} fichier(s) — " +
                    $"{result.TotalSizeBytes / 1024.0 / 1024.0:N2} Mo";

                if (!result.IsComplete)
                {
                    InventorySummary += " — Inventaire incomplet";
                }
            }
            catch (Exception ex) when (
                ex is UnauthorizedAccessException
                or System.IO.IOException
                or ArgumentException
                or OverflowException)
            {
                InvalidateReport("Échec de l'inventaire. Relancez la vérification.");
                _inventory = null;
                InventoryFiles.Clear();

                InventoryWarnings.Add(
                    $"Erreur d'inventaire : {ex.Message}");

                InventorySummary = "Échec de l'inventaire";
            }
        }

        private void Verify()
        {
            ClearReport();
            RuleVersion = "Non chargée";
            Findings.Clear();

            Status = "ANALYSE EN COURS";
            Details = "Chargement du référentiel C411...";
            AllowConformingExport = false;

            try
            {
                string analyzedInputs = CaptureInputs();
                var service = new ComplianceService();

                // L'analyse reste possible sans inventaire, sans certification.
                IReadOnlyList<ReleaseFile> files =
                 _inventory?.Files ?? Array.Empty<ReleaseFile>();

                ComplianceReport report =
                    service.Analyze(_metadata, files);
                Status = report.Status switch
                {
                    ComplianceStatus.Conforme => "CONFORME",
                    ComplianceStatus.AVerifier => "À VÉRIFIER",
                    ComplianceStatus.NonConforme => "NON CONFORME",
                    _ => "NON ANALYSÉ"
                };

                RuleVersion = report.RuleSetVersion;

                // Export conforme volontairement désactivé
                // tant que l'inventaire n'est pas disponible.
                AllowConformingExport = false;

                foreach (Finding finding in report.Findings)
                {
                    Findings.Add(
                        $"[{finding.RuleId}] " +
                        $"{finding.Status} — {finding.Message}");

                    if (!string.IsNullOrWhiteSpace(finding.Correction))
                    {
                        Findings.Add(
                            $"Correction : {finding.Correction}");
                    }
                }

                if (report.Findings.Count == 0)
                {
                    Findings.Add(
                        "Aucune anomalie signalée par les contrôles exécutés.");
                }

                // Ne jamais certifier une release
                // sur la seule base des métadonnées.
                if (report.Status == ComplianceStatus.Conforme)
                {
                    Status = "À VÉRIFIER";

                    Details =
                        "Aucune violation détectée par les contrôles exécutés. " +
                        "Cependant, la conformité complète ne peut pas encore " +
                        "être établie : certaines déclarations de sécurité, " +
                        "de provenance et de classification restent à vérifier.";

                    Findings.Add(
                        "Vérification complémentaire nécessaire : " +
                        "l'inventaire des fichiers ne constitue pas " +
                        "une certification de conformité.");
                }
                else
                {
                    Details =
                        $"Analyse terminée : {report.Findings.Count} " +
                        "élément(s) signalé(s).";

                    if (_inventory is null || !_inventory.IsComplete)
                    {
                        Details +=
                            " L'inventaire est absent ou incomplet.";
                    }
                }
                // Publier uniquement après une analyse entièrement réussie.
                if (!string.Equals(analyzedInputs, CaptureInputs(), StringComparison.Ordinal))
                {
                    InvalidateReport("Données modifiées pendant l'analyse. Relancez la vérification.");
                    return;
                }
                _analyzedInputs = analyzedInputs;
                _lastReport = report;
                ((RelayCommand)ExportReportCommand).RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                ClearReport();
                RuleVersion = "Non chargée";
                Status = "ERREUR D'ANALYSE";

                Details =
                    "Impossible d'effectuer le contrôle : " +
                    ex.Message;

                Findings.Add(Details);
            }
        }

        private void ExportReport()
        {
            if (!EnsureReportCurrent())
                return;

            try
            {
                string softwareName =
                    string.IsNullOrWhiteSpace(_metadata.Name)
                        ? "Logiciel"
                        : _metadata.Name.Trim();

                foreach (char invalidChar in Path.GetInvalidFileNameChars())
                {
                    softwareName = softwareName.Replace(invalidChar, '_');
                }

                softwareName = softwareName.TrimEnd(' ', '.');

                if (string.IsNullOrWhiteSpace(softwareName))
                    softwareName = "Logiciel";

                string date = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                string baseFileName =
                    $"NFOForge_C411_{softwareName}_{date}";

                var dialog = new SaveFileDialog
                {
                    Title = "Exporter le rapport de conformité",
                    Filter =
                        "Rapport JSON (*.json)|*.json|" +
                        "Rapport texte (*.txt)|*.txt",
                    FilterIndex = 1,
                    DefaultExt = ".json",
                    AddExtension = true,
                    FileName = baseFileName + ".json",
                    OverwritePrompt = true
                };

                if (_showSaveDialog(dialog) != true)
                    return;

                // Le dialogue modal peut laisser passer des modifications des données.
                if (!EnsureReportCurrent())
                    return;

                // Le format choisi dans la boîte de dialogue
                // détermine le service d'exportation.
                if (dialog.FilterIndex == 2)
                {
                    string destination = Path.ChangeExtension(
                        dialog.FileName, ".txt");

                    _exportService.ExportTxt(
                        destination,
                        _metadata,
                        _lastReport!,
                        _inventory,
                        Status);

                    Details = "Rapport TXT exporté : " + destination;
                }
                else
                {
                    string destination = Path.ChangeExtension(
                        dialog.FileName, ".json");

                    _exportService.ExportJson(
                        destination,
                        _metadata,
                        _lastReport!,
                        _inventory,
                        Status);

                    Details = "Rapport JSON exporté : " + destination;
                }
            }
            catch (Exception ex) when (
                ex is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or System.Security.SecurityException)
            {
                Details =
                    "Erreur lors de l'exportation : " + ex.Message;
            }
        }

        // Compare les données chargées, y compris les listes de l'inventaire.
        // Aucun fichier du dossier n'est exécuté ni surveillé en arrière-plan.
        private string CaptureInputs() => JsonSerializer.Serialize(new
        {
            Metadata = _metadata,
            Inventory = _inventory
        });

        private bool IsReportCurrent() => _lastReport is not null
            && string.Equals(_analyzedInputs, CaptureInputs(), StringComparison.Ordinal);

        private bool EnsureReportCurrent()
        {
            if (IsReportCurrent()) return true;
            InvalidateReport("Rapport absent ou périmé. Relancez la vérification avant l'exportation.");
            return false;
        }

        private void ClearReport()
        {
            _lastReport = null;
            _analyzedInputs = null;
            ((RelayCommand)ExportReportCommand).RaiseCanExecuteChanged();
        }

        private void InvalidateReport(string message)
        {
            ClearReport();
            AllowConformingExport = false;
            Status = "NON ANALYSÉ";
            Details = message;
            RuleVersion = "Non chargée";
            Findings.Clear();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(
            [CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));
        }
    }
}
