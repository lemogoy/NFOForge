using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using NFOForge.App.Commands;
using NFOForge.App.Models;
using NFOForge.App.Services;
using NFOForge.App.Views;

namespace NFOForge.App.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private object? _currentView;
        private string _currentPage = "Dashboard";
        private string? _projectPath;
        private NFOForgeProject _projectIdentity = new();
        private string _savedFingerprint = string.Empty;
        private string _projectStatus = "Nouveau projet";

        private readonly SoftwareMetadata _metadata;
        private readonly MetadataViewModel _metadataViewModel;
        private readonly ComplianceViewModel _complianceViewModel;
        private readonly NfoEditorViewModel _nfoEditorViewModel;
        private readonly BBCodeEditorViewModel _bbCodeEditorViewModel;
        private readonly BBCodeEditorView _bbCodeEditorView;
        private readonly NFOForgeProjectStorageService _projectStorage = new();

        public object? CurrentView
        {
            get => _currentView;
            private set { if (ReferenceEquals(_currentView, value)) return; _currentView = value; OnPropertyChanged(); }
        }

        public string CurrentPage
        {
            get => _currentPage;
            private set { if (_currentPage == value) return; _currentPage = value; OnPropertyChanged(); }
        }

        public string ProjectStatus
        {
            get => _projectStatus;
            private set { if (_projectStatus == value) return; _projectStatus = value; OnPropertyChanged(); }
        }

        public ICommand NavigateCommand { get; }
        public ICommand NewProjectCommand { get; }
        public ICommand OpenProjectCommand { get; }
        public ICommand SaveProjectCommand { get; }
        public ICommand SaveProjectAsCommand { get; }

        public MainViewModel()
        {
            _metadata = new SoftwareMetadata();
            _metadataViewModel = new MetadataViewModel(_metadata);
            _complianceViewModel = new ComplianceViewModel(_metadata);
            _nfoEditorViewModel = new NfoEditorViewModel(_metadata);
            _bbCodeEditorViewModel = new BBCodeEditorViewModel(_metadata);
            // Conserver une seule instance : FlowDocument ne supporte pas deux viewers.
            _bbCodeEditorView = new BBCodeEditorView { DataContext = _bbCodeEditorViewModel };

            NavigateCommand = new RelayCommand(Navigate);
            NewProjectCommand = new RelayCommand(_ => NewProject());
            OpenProjectCommand = new RelayCommand(_ => OpenProject());
            SaveProjectCommand = new RelayCommand(_ => SaveProject(false));
            SaveProjectAsCommand = new RelayCommand(_ => SaveProject(true));

            _savedFingerprint = Fingerprint(CaptureProject());
            Navigate("Dashboard");
        }

        // Capture déterministe : aucune mise à jour des horodatages lors d'une simple lecture.
        private NFOForgeProject CaptureProject() => new()
        {
            ProjectId = _projectIdentity.ProjectId,
            CreatedAtUtc = _projectIdentity.CreatedAtUtc,
            ModifiedAtUtc = _projectIdentity.ModifiedAtUtc,
            Metadata = ProjectMetadata.From(_metadata),
            NfoText = _nfoEditorViewModel.NfoText,
            NfoEncoding = _nfoEditorViewModel.SelectedEncoding,
            NfoTemplate = _nfoEditorViewModel.SelectedTemplate,
            BBCodeText = _bbCodeEditorViewModel.BBCodeText,
            BBCodeTemplate = _bbCodeEditorViewModel.SelectedTemplate,
            BBCodeCustomValues = _bbCodeEditorViewModel.CaptureCustomValues(),
            PosterUrl = _bbCodeEditorViewModel.PosterUrl,
            ScreenshotUrl = _bbCodeEditorViewModel.ScreenshotUrl
        };

        private static string Fingerprint(NFOForgeProject p) => JsonSerializer.Serialize(new
        {
            p.Metadata,
            p.NfoText,
            p.NfoEncoding,
            p.NfoTemplate,
            p.BBCodeText,
            p.BBCodeTemplate,
            p.BBCodeCustomValues,
            p.PosterUrl,
            p.ScreenshotUrl
        });

        private bool HasChanges() =>
            _nfoEditorViewModel.HasUnsavedTemplateChanges ||
            _bbCodeEditorViewModel.HasUnsavedTemplateChanges ||
            !string.Equals(Fingerprint(CaptureProject()), _savedFingerprint, StringComparison.Ordinal);

        private bool ConfirmReplaceProject() => !HasChanges() ||
            MessageBox.Show(
                "Des modifications non enregistrées seront perdues. Continuer ?",
                "NFOForge — Projet modifié", MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

        private static bool IsProjectError(Exception ex) =>
            ex is IOException or UnauthorizedAccessException or ArgumentException
                or InvalidOperationException or JsonException or SecurityException;

        private void NewProject()
        {
            if (!ConfirmReplaceProject()) return;
            var project = new NFOForgeProject();
            ApplyProject(project);
            _projectIdentity = project;
            _projectPath = null;
            _savedFingerprint = Fingerprint(CaptureProject());
            ProjectStatus = "Nouveau projet";
            Navigate("Metadata");
        }

        private void OpenProject()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Ouvrir un projet NFOForge",
                Filter = "Projet NFOForge (*.nfoproject)|*.nfoproject",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog() != true) return;
            try
            {
                // Lecture ET validation avant toute modification des éditeurs.
                NFOForgeProject project = _projectStorage.Load(dialog.FileName);
                if (!ConfirmReplaceProject()) return;
                ApplyProject(project);
                _projectIdentity = project;
                _projectPath = dialog.FileName;
                _savedFingerprint = Fingerprint(CaptureProject());
                ProjectStatus = "Projet ouvert : " + Path.GetFileName(_projectPath);
                Navigate("Metadata");
            }
            catch (Exception ex) when (IsProjectError(ex))
            {
                MessageBox.Show("Ouverture impossible : " + ex.Message,
                    "NFOForge", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Autorise ou annule la fermeture. Oui enregistre le projet, Non abandonne
        /// les changements, Annuler reste dans l'application.
        /// </summary>
        public bool ConfirmCloseProject()
        {
            if (!HasChanges()) return true;

            MessageBoxResult choice = MessageBox.Show(
                "Le projet comporte des modifications non enregistrées.\n\n" +
                "Oui : enregistrer avant de quitter.\n" +
                "Non : quitter sans enregistrer.\n" +
                "Annuler : revenir dans NFOForge.",
                "NFOForge — Fermeture du projet",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Cancel);

            return choice switch
            {
                MessageBoxResult.Yes => SaveProject(false),
                MessageBoxResult.No => true,
                _ => false
            };
        }

        // Renvoie false si la sauvegarde échoue ou est annulée : la fermeture est bloquée.
        private bool SaveProject(bool saveAs)
        {
            if (_nfoEditorViewModel.HasUnsavedTemplateChanges ||
                _bbCodeEditorViewModel.HasUnsavedTemplateChanges)
            {
                MessageBox.Show(
                    "Enregistrez ou abandonnez les modifications des modèles avant de sauvegarder le projet.",
                    "NFOForge", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            string? path = saveAs ? null : _projectPath;
            if (path is null)
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Enregistrer le projet NFOForge",
                    Filter = "Projet NFOForge (*.nfoproject)|*.nfoproject",
                    DefaultExt = ".nfoproject",
                    AddExtension = true,
                    FileName = _projectPath is null ? "MonProjet.nfoproject" : Path.GetFileName(_projectPath),
                    OverwritePrompt = true
                };
                if (dialog.ShowDialog() != true) return false;
                path = dialog.FileName;
            }

            try
            {
                NFOForgeProject project = CaptureProject();
                _projectStorage.Save(path, project);
                _projectIdentity = project;
                _projectPath = path;
                _savedFingerprint = Fingerprint(CaptureProject());
                ProjectStatus = "Projet enregistré : " + Path.GetFileName(path);
                return true;
            }
            catch (Exception ex) when (IsProjectError(ex))
            {
                MessageBox.Show("Enregistrement impossible : " + ex.Message,
                    "NFOForge", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void ApplyProject(NFOForgeProject project)
        {
            // Validate préalablement, les setters des éditeurs ne montrent pas de confirmation.
            NFOForgeProjectStorageService.Validate(project);
            project.Metadata.ApplyTo(_metadata);
            _nfoEditorViewModel.RestoreProjectState(
                project.NfoText, project.NfoEncoding, project.NfoTemplate);
            _bbCodeEditorViewModel.RestoreProjectState(
                project.BBCodeText, project.BBCodeTemplate,
                project.BBCodeCustomValues, project.PosterUrl, project.ScreenshotUrl);
        }

        private void Navigate(object? parameter)
        {
            string? page = parameter?.ToString();
            if (string.IsNullOrWhiteSpace(page)) return;
            object? nextView = page switch
            {
                "Dashboard" => new DashboardView(),
                "NewProject" => new NewProjectView(),
                "Metadata" => CreateMetadataView(),
                "Compliance" => CreateComplianceView(),
                "NFO" => CreateNfoEditorView(),
                "BBCode" => _bbCodeEditorView,
                "Preview" => new PreviewView(),
                "Settings" => new SettingsView(),
                "About" => new AboutView(),
                _ => null
            };
            if (nextView is null) return;
            CurrentView = nextView;
            CurrentPage = page;
        }

        private MetadataView CreateMetadataView() => new() { DataContext = _metadataViewModel };
        private ComplianceView CreateComplianceView() => new() { DataContext = _complianceViewModel };
        private NfoEditorView CreateNfoEditorView() => new() { DataContext = _nfoEditorViewModel };

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
