using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using NFOForge.App.Commands;
using NFOForge.App.Models;
using NFOForge.App.Services;
using System.Linq;
using System.Collections.ObjectModel;

namespace NFOForge.App.ViewModels
{
    public sealed class NfoEditorViewModel : INotifyPropertyChanged
    {
        private readonly SoftwareMetadata _metadata;

        private readonly NfoFileSaveService _saveService = new();

        private string _nfoText = string.Empty;
        private string _selectedEncoding = "UTF-8 sans BOM";
        private string _statusMessage = "Éditeur NFO prêt.";

        // Le dernier état enregistré tient compte du texte ET de l'encodage.
        private string _savedText = string.Empty;
        private string _savedEncoding = "UTF-8 sans BOM";
        private bool _isModified;
        private string _selectedTemplate = "Classique";

        public ObservableCollection<string> AvailableTemplates { get; } =
             [
                 "Classique",
                "Moderne",
                "ASCII Art"
             ];

        // Le choix d'un modèle n'efface jamais une édition non sauvegardée sans accord.
        public string SelectedTemplate
        {
            get => _selectedTemplate;
            set
            {
                string requested = value ?? "Classique";
                if (string.Equals(_selectedTemplate, requested, StringComparison.Ordinal)) return;
                if (!ConfirmDiscardTemplateChanges("Changer de modèle"))
                {
                    OnPropertyChanged(nameof(SelectedTemplate));
                    StatusMessage = "Changement de modèle annulé.";
                    return;
                }

                _selectedTemplate = requested;
                OnPropertyChanged();
                LoadSelectedTemplateIntoEditor();
            }
        }

        private string? _editingTemplateId;
        private string _savedTemplateName = string.Empty;
        private string _savedTemplateContent = string.Empty;
        private bool _isTemplateModified;
        private bool _isLoadingTemplate;
        public bool IsTemplateModified => _isTemplateModified;
        public string TemplateState => IsTemplateModified ? "● Modèle modifié" : "✓ Modèle enregistré";

        private void UpdateTemplateModifiedState()
        {
            if (_isLoadingTemplate) return;
            bool modified = !string.Equals(TemplateName, _savedTemplateName, StringComparison.Ordinal)
                || !string.Equals(TemplateContent, _savedTemplateContent, StringComparison.Ordinal);
            if (_isTemplateModified == modified) return;
            _isTemplateModified = modified;
            OnPropertyChanged(nameof(IsTemplateModified));
            OnPropertyChanged(nameof(TemplateState));
        }

        private void SetTemplateEditor(string? id, string name, string content)
        {
            _isLoadingTemplate = true;
            try
            {
                _editingTemplateId = id;
                TemplateName = name;
                TemplateContent = content;
                _savedTemplateName = name;
                _savedTemplateContent = content;
            }
            finally { _isLoadingTemplate = false; }
            UpdateTemplateModifiedState();
            TemplatePreview = string.Empty;
        }

        private bool ConfirmDiscardTemplateChanges(string action)
        {
            if (!IsTemplateModified) return true;
            return MessageBox.Show(
                $"{action} abandonnera les modifications non enregistrées du modèle.\n\nContinuer ?",
                "NFOForge — Modèle non enregistré",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        private void LoadSelectedTemplateIntoEditor()
        {
            NfoTemplate? template = FindCustomTemplate();
            if (template is null)
                SetTemplateEditor(null, string.Empty, string.Empty);
            else
                SetTemplateEditor(template.Id, template.Name, template.Content);
        }


        private readonly NfoTemplateService _templateService = new();
        private readonly NfoTemplateStorageService _templateStorage = new();

        private readonly List<NfoTemplate> _customTemplates = new();

        private string _templateName = string.Empty;
        private string _templateContent = string.Empty;
        private string _templatePreview = string.Empty;

        public string TemplateName
        {
            get => _templateName;
            set
            {
                if (_templateName == value) return;
                _templateName = value ?? string.Empty;
                OnPropertyChanged();
                UpdateTemplateModifiedState();
            }
        }

        public string TemplateContent
        {
            get => _templateContent;
            set
            {
                if (_templateContent == value) return;
                _templateContent = value ?? string.Empty;
                OnPropertyChanged();
                UpdateTemplateModifiedState();
            }
        }

        public string TemplatePreview
        {
            get => _templatePreview;
            private set
            {
                if (_templatePreview == value) return;
                _templatePreview = value;
                OnPropertyChanged();
            }
        }

        public ICommand NewTemplateCommand { get; private set; } = null!;
        public ICommand SaveTemplateCommand { get; private set; } = null!;
        public ICommand DeleteTemplateCommand { get; private set; } = null!;
        public ICommand PreviewTemplateCommand { get; private set; } = null!;

        //C — Connecter les commandes//

        public bool IsModified => _isModified;

        public string DocumentState => IsModified ? "● Modifié" : "✓ Enregistré";

        private void UpdateModifiedState()
        {
            bool modified = !string.Equals(NfoText, _savedText, StringComparison.Ordinal)
                || !string.Equals(SelectedEncoding, _savedEncoding, StringComparison.Ordinal);

            if (_isModified == modified)
                return;

            _isModified = modified;
            OnPropertyChanged(nameof(IsModified));
            OnPropertyChanged(nameof(DocumentState));
        }

        private void MarkSaved()
        {
            _savedText = NfoText;
            _savedEncoding = SelectedEncoding;
            UpdateModifiedState();
        }

        private bool ConfirmDiscardChanges(string action)
        {
            if (!IsModified)
                return true;

            return MessageBox.Show(
                $"{action} remplacera les modifications non enregistrées.\n\nContinuer ?",
                "NFOForge — Modifications non enregistrées",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        public IReadOnlyList<string> AvailableEncodings { get; } =
        [
            "UTF-8 sans BOM",
            "UTF-8 avec BOM",
            "CP437"
        ];

        public string NfoText
        {
            get => _nfoText;
            set
            {
                string text = value ?? string.Empty;

                if (_nfoText == text)
                    return;

                _nfoText = text;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CharacterCount));
                OnPropertyChanged(nameof(LineCount));
                UpdateModifiedState();
            }
        }

        public string SelectedEncoding
        {
            get => _selectedEncoding;
            set
            {
                if (_selectedEncoding == value)
                    return;

                _selectedEncoding = value;
                OnPropertyChanged();
                UpdateModifiedState();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                if (_statusMessage == value)
                    return;

                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public int CharacterCount => NfoText.Length;

        public int LineCount =>
            string.IsNullOrEmpty(NfoText)
                ? 0
                : NfoText.Split('\n').Length;

        public ICommand GenerateCommand { get; }
        public ICommand ImportCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand CopyCommand { get; }

        public NfoEditorViewModel(SoftwareMetadata metadata)
        {
            _metadata = metadata
                ?? throw new ArgumentNullException(nameof(metadata));

            Encoding.RegisterProvider(
                CodePagesEncodingProvider.Instance);

            GenerateCommand = new RelayCommand(
                _ => Generate());

            ImportCommand = new RelayCommand(
                _ => Import());

            ExportCommand = new RelayCommand(
                _ => Export());

            CopyCommand = new RelayCommand(
                _ => Copy());

            NewTemplateCommand = new RelayCommand(
                _ => NewTemplate());

            SaveTemplateCommand = new RelayCommand(
                _ => SaveTemplate());

            DeleteTemplateCommand = new RelayCommand(
                _ => DeleteTemplate());

            PreviewTemplateCommand = new RelayCommand(
                _ => PreviewTemplate());

            LoadCustomTemplates();
        }

        private void LoadCustomTemplates()
        {
            try
            {
                var loaded = _templateStorage.Load();
                _customTemplates.Clear();
                // La liste n'est modifiée qu'après un chargement et une validation réussis.
                while (AvailableTemplates.Count > 3)
                    AvailableTemplates.RemoveAt(3);
                foreach (NfoTemplate item in loaded)
                {
                    _customTemplates.Add(item);
                    AvailableTemplates.Add(item.Name);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or ArgumentException or InvalidOperationException
                or System.Security.SecurityException)
            {
                StatusMessage = "Modèles non chargés (fichier conservé) : " + ex.Message;
            }
        }

        private NfoTemplate? FindCustomTemplate() => _customTemplates.FirstOrDefault(t =>
            string.Equals(t.Name, SelectedTemplate, StringComparison.OrdinalIgnoreCase));

        private void NewTemplate()
        {
            if (!ConfirmDiscardTemplateChanges("Créer un modèle")) return;
            // Édition d'un brouillon autonome, sans modifier le modèle sélectionné.
            SetTemplateEditor(null, string.Empty,
                "Nom     : {Nom}\nVersion : {Version}\n\n{Description}");
            // Un nouveau brouillon doit apparaître comme non enregistré.
            _savedTemplateContent = string.Empty;
            UpdateTemplateModifiedState();
            StatusMessage = "Nouveau brouillon : renseignez son nom puis enregistrez.";
        }

        private void SaveTemplate()
        {
            try
            {
                string name = TemplateName.Trim();
                if (_editingTemplateId is null)
                {
                    NfoTemplate created = _templateStorage.Create(name, TemplateContent);
                    _customTemplates.Add(created);
                    AvailableTemplates.Add(created.Name);
                    _selectedTemplate = created.Name;
                    OnPropertyChanged(nameof(SelectedTemplate));
                    SetTemplateEditor(created.Id, created.Name, created.Content);
                }
                else
                {
                    NfoTemplate existing = _customTemplates.FirstOrDefault(t => t.Id == _editingTemplateId)
                        ?? throw new InvalidOperationException("Le modèle sélectionné est introuvable.");
                    string previousName = existing.Name;
                    var updated = new NfoTemplate
                    {
                        Id = existing.Id,
                        Name = name,
                        Content = TemplateContent,
                        IsBuiltIn = false
                    };
                    _templateStorage.Update(updated);
                    int index = AvailableTemplates.IndexOf(previousName);
                    if (index >= 0) AvailableTemplates[index] = name;
                    existing.Name = name;
                    existing.Content = TemplateContent;
                    _selectedTemplate = name;
                    OnPropertyChanged(nameof(SelectedTemplate));
                    SetTemplateEditor(existing.Id, existing.Name, existing.Content);
                }
                StatusMessage = "Modèle NFO enregistré.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or ArgumentException or InvalidOperationException
                or System.Security.SecurityException)
            {
                StatusMessage = "Enregistrement impossible : " + ex.Message;
            }
        }

        private void DeleteTemplate()
        {
            NfoTemplate? template = _editingTemplateId is null ? null :
                _customTemplates.FirstOrDefault(t => t.Id == _editingTemplateId);
            if (template is null)
            {
                StatusMessage = "Sélectionnez un modèle personnalisé enregistré à supprimer.";
                return;
            }
            if (!ConfirmDiscardTemplateChanges("Supprimer le modèle")) return;
            if (MessageBox.Show(
                $"Supprimer définitivement le modèle « {template.Name} » ?",
                "NFOForge — Suppression", MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
            try
            {
                _templateStorage.Delete(template.Id);
                _customTemplates.Remove(template);
                AvailableTemplates.Remove(template.Name);
                _selectedTemplate = "Classique";
                OnPropertyChanged(nameof(SelectedTemplate));
                SetTemplateEditor(null, string.Empty, string.Empty);
                StatusMessage = "Modèle supprimé.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or ArgumentException or InvalidOperationException
                or System.Security.SecurityException)
            {
                StatusMessage = "Suppression impossible : " + ex.Message;
            }
        }


        private void PreviewTemplate()
        {
            try
            {
                var template = new NfoTemplate
                {
                    Name = TemplateName,
                    Content = TemplateContent,
                    IsBuiltIn = false
                };

                TemplatePreview = _templateService.Render(
                    template,
                    _metadata);

                StatusMessage = "Prévisualisation du modèle actualisée.";
            }
            catch (ArgumentException ex)
            {
                TemplatePreview = string.Empty;
                StatusMessage = ex.Message;
            }
        }
        private void Generate()
        {
            if (string.IsNullOrWhiteSpace(_metadata.Name))
            {
                StatusMessage =
                    "Renseignez le nom du logiciel dans Métadonnées.";
                return;
            }

            if (!ConfirmDiscardChanges("La génération"))
            {
                StatusMessage = "Génération annulée.";
                return;
            }

            var customTemplate = FindCustomTemplate();

            if (customTemplate is not null)
            {
                try
                {
                    string generated = _templateService.Render(
                        customTemplate,
                        _metadata);

                    NfoText = generated;

                    StatusMessage =
                        $"NFO généré avec le modèle {customTemplate.Name}.";
                }
                catch (ArgumentException ex)
                {
                    StatusMessage =
                        "Erreur de génération : " + ex.Message;
                }

                return;
            }
            var sb = new StringBuilder();

            switch (SelectedTemplate)
            {
                case "Moderne":
                    sb.AppendLine("╔════════════════════════════════════════════════╗");
                    sb.AppendLine("║                    NFOFORGE                    ║");
                    sb.AppendLine("╚════════════════════════════════════════════════╝");
                    break;

                case "ASCII Art":
                    sb.AppendLine("██████╗  NFOFORGE  ╔██████");
                    sb.AppendLine("▓▓▓▓▓▓║  NFO FILE  ║▓▓▓▓▓▓");
                    sb.AppendLine("▒▒▒▒▒▒╚════════════╝▒▒▒▒▒▒");
                    break;

                default:
                    sb.AppendLine("+------------------------------------------------+");
                    sb.AppendLine("|                    NFOFORGE                    |");
                    sb.AppendLine("+------------------------------------------------+");
                    break;
            }

            sb.AppendLine();

            AddField(sb, "Nom", _metadata.Name);
            AddField(sb, "Version", _metadata.Version);
            AddField(sb, "Editeur", _metadata.Publisher);
            AddField(sb, "Developpeur", _metadata.Developer);
            AddField(sb, "Categorie", _metadata.Category);
            AddField(sb, "Sous-categorie", _metadata.Subcategory);
            AddField(sb, "Systeme", _metadata.OperatingSystem);
            AddField(sb, "Architecture", _metadata.Architecture);
            AddField(sb, "Langue", _metadata.Language);

            if (_metadata.ReleaseDate.HasValue)
            {
                AddField(
                    sb,
                    "Date de sortie",
                    _metadata.ReleaseDate.Value.ToString("dd/MM/yyyy"));
            }

            AddSection(sb, "DESCRIPTION", _metadata.Description);
            AddSection(sb, "FONCTIONNALITES", _metadata.Features);
            AddSection(
                sb,
                "INFORMATIONS COMPLEMENTAIRES",
                _metadata.AdditionalInformation);

            sb.AppendLine("--------------------------------------------------");

            NfoText = sb.ToString();
            StatusMessage = $"NFO généré avec le modèle {SelectedTemplate}.";
        }

        private static void AddField(
            StringBuilder sb,
            string label,
            string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            sb.AppendLine($"{label,-18}: {value.Trim()}");
        }

        private static void AddSection(
            StringBuilder sb,
            string title,
            string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return;

            sb.AppendLine();
            sb.AppendLine($"[ {title} ]");
            sb.AppendLine();

            sb.AppendLine(content.Trim());
            sb.AppendLine();
        }

        private Encoding GetSelectedEncoding()
        {
            return SelectedEncoding switch
            {
                "CP437" => Encoding.GetEncoding(
                    437,
                    EncoderFallback.ExceptionFallback,
                    DecoderFallback.ExceptionFallback),

                "UTF-8 avec BOM" => new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: true,
                    throwOnInvalidBytes: true),

                _ => new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true)
            };
        }


        private void Import()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Importer un fichier NFO",
                Filter = "Fichiers NFO (*.nfo)|*.nfo|" +
                         "Fichiers texte (*.txt)|*.txt|" +
                         "Tous les fichiers (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                byte[] content = File.ReadAllBytes(dialog.FileName);

                bool hasUtf8Bom =
                    content.Length >= 3 &&
                    content[0] == 0xEF &&
                    content[1] == 0xBB &&
                    content[2] == 0xBF;

                Encoding encoding = hasUtf8Bom
                    ? new UTF8Encoding(false, true)
                    : GetSelectedEncoding();

                int offset = hasUtf8Bom ? 3 : 0;

                string importedText = encoding.GetString(
                    content,
                    offset,
                    content.Length - offset);

                if (!ConfirmDiscardChanges("L'importation"))
                {
                    StatusMessage = "Importation annulée.";
                    return;
                }

                NfoText = importedText;

                if (hasUtf8Bom)
                    SelectedEncoding = "UTF-8 avec BOM";

                // Un fichier importé constitue la nouvelle référence propre.
                MarkSaved();

                StatusMessage =
                    "NFO importé : " +
                    Path.GetFileName(dialog.FileName);
            }
            catch (Exception ex) when (
                ex is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or DecoderFallbackException
                or System.Security.SecurityException)
            {
                StatusMessage =
                    "Erreur d'importation : " + ex.Message;
            }
        }



        private void Export()
        {
            if (string.IsNullOrWhiteSpace(NfoText))
            {
                StatusMessage = "Le contenu NFO est vide.";
                return;
            }

            try
            {
                Encoding encoding = GetSelectedEncoding();

                // Vérifier l'encodage avant toute écriture.
                byte[] content = encoding.GetBytes(NfoText);
                byte[] preamble = encoding.GetPreamble();

                byte[] output = new byte[preamble.Length + content.Length];

                Buffer.BlockCopy(
                    preamble, 0,
                    output, 0,
                    preamble.Length);

                Buffer.BlockCopy(
                    content, 0,
                    output, preamble.Length,
                    content.Length);

                var dialog = new SaveFileDialog
                {
                    Title = "Exporter le fichier NFO",
                    Filter = "Fichiers NFO (*.nfo)|*.nfo",
                    DefaultExt = ".nfo",
                    AddExtension = true,
                    FileName = "NFOForge.nfo",
                    OverwritePrompt = true
                };

                if (dialog.ShowDialog() != true)
                    return;

                _saveService.Save(dialog.FileName, output);

                MarkSaved();

                StatusMessage =
                    "NFO exporté (" + SelectedEncoding + ") : " +
                    Path.GetFileName(dialog.FileName);
            }
            catch (EncoderFallbackException)
            {
                StatusMessage =
                    "Export refusé : certains caractères " +
                    "ne sont pas compatibles avec CP437. " +
                    "Sélectionnez UTF-8.";
            }
            catch (Exception ex) when (
                ex is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or OverflowException
                or System.Security.SecurityException)
            {
                StatusMessage =
                    "Erreur d'exportation : " + ex.Message;
            }
        }


        private void Copy()
        {
            try
            {
                Clipboard.SetText(NfoText);
                StatusMessage =
                    "Contenu NFO copié dans le presse-papiers.";
            }
            catch (Exception ex) when (
                ex is System.Runtime.InteropServices.ExternalException
                or ArgumentException)
            {
                StatusMessage =
                    "Impossible de copier le texte : " + ex.Message;
            }
        }

        // Étape 27.2 : restauration explicite, sans dialogue intermédiaire.
        // La confirmation de perte de données est centralisée dans MainViewModel.
        public bool HasUnsavedTemplateChanges => IsTemplateModified;

        public void RestoreProjectState(string text, string encoding, string templateName)
        {
            if (!AvailableEncodings.Contains(encoding))
                throw new ArgumentException("Encodage NFO du projet non reconnu.", nameof(encoding));
            string effectiveTemplate = AvailableTemplates.Contains(templateName)
                ? templateName : "Classique";

            _selectedTemplate = effectiveTemplate;
            OnPropertyChanged(nameof(SelectedTemplate));
            LoadSelectedTemplateIntoEditor();
            SelectedEncoding = encoding;
            NfoText = text ?? string.Empty;
            MarkSaved();
            StatusMessage = effectiveTemplate == templateName
                ? "NFO restauré depuis le projet."
                : $"Modèle NFO « {templateName} » absent : modèle Classique sélectionné.";
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
