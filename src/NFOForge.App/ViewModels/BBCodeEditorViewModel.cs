using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Documents;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using NFOForge.App.Commands;
using NFOForge.App.Models;
using NFOForge.App.Services;

namespace NFOForge.App.ViewModels
{
    public sealed class BBCodeEditorViewModel : INotifyPropertyChanged
    {
        private const long MaxImportBytes = 2 * 1024 * 1024;
        private readonly SoftwareMetadata _metadata;
        private readonly NfoFileSaveService _saveService = new();
        private readonly BBCodeRenderService _renderService = new();
        private readonly BBCodeValidationService _validationService = new();
        private readonly DispatcherTimer _previewTimer = new()
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        private FlowDocument _previewDocument = new();

        public FlowDocument PreviewDocument
        {
            get => _previewDocument;
            private set
            {
                if (ReferenceEquals(_previewDocument, value)) return;
                _previewDocument = value;
                OnPropertyChanged();
            }
        }

        private string _bbCodeText = string.Empty;
        private string _savedText = string.Empty;
        private string _statusMessage = "Éditeur BBCode prêt.";
        private string _validationMessage = "Validation non effectuée.";
        private string _selectedTemplate = "Classique";

        private readonly BBCodeTemplateService _templateService = new();
        private readonly BBCodeTemplateStorageService _templateStorage = new();
        private readonly List<BBCodeTemplate> _customTemplates = new();
        private string? _editingTemplateId;
        private bool _isTemplateDraft;
        private string _templateName = string.Empty;
        private string _templateContent = string.Empty;
        private string _savedTemplateName = string.Empty;
        private string _savedTemplateContent = string.Empty;
        private string _templatePreview = string.Empty;
        private string _posterUrl = string.Empty;
        private string _screenshotUrl = string.Empty;

        // Valeurs temporaires du formulaire : elles ne sont pas persistées en JSON.
        public ObservableCollection<BBCodeCustomVariable> CustomVariables { get; } = new();

        public string PosterUrl
        {
            get => _posterUrl;
            set { if (_posterUrl == value) return; _posterUrl = value ?? string.Empty; OnPropertyChanged(); }
        }

        public string ScreenshotUrl
        {
            get => _screenshotUrl;
            set { if (_screenshotUrl == value) return; _screenshotUrl = value ?? string.Empty; OnPropertyChanged(); }
        }

        private void RefreshCustomVariables()
        {
            try
            {
                var oldValues = CustomVariables.ToDictionary(
                    item => item.Name, item => item.Value,
                    StringComparer.OrdinalIgnoreCase);

                var names = _templateService.GetCustomVariables(TemplateContent);
                CustomVariables.Clear();
                foreach (string name in names)
                {
                    oldValues.TryGetValue(name, out string? value);
                    CustomVariables.Add(new BBCodeCustomVariable(name, value ?? string.Empty));
                }
            }
            catch (ArgumentException ex)
            {
                StatusMessage = "Variables du modèle : " + ex.Message;
            }
            catch (RegexMatchTimeoutException)
            {
                StatusMessage = "Analyse des variables interrompue (délai dépassé).";
            }
        }

        private IReadOnlyDictionary<string, string> GetCustomValues() =>
            CustomVariables.ToDictionary(
                item => item.Name, item => item.Value,
                StringComparer.OrdinalIgnoreCase);


        public ObservableCollection<string> AvailableTemplates { get; } =
            new() { "Classique", "Moderne" };

        public string SelectedTemplate
        {
            get => _selectedTemplate;
            set
            {
                string next = value ?? "Classique";
                if (_selectedTemplate == next) return;
                if (!ConfirmDiscardTemplateChanges("Changer de modèle"))
                {
                    OnPropertyChanged(nameof(SelectedTemplate));
                    return;
                }

                _selectedTemplate = next;
                OnPropertyChanged();
                LoadTemplateEditor(next);
            }
        }

        public string TemplateName
        {
            get => _templateName;
            set
            {
                string next = value ?? string.Empty;
                if (_templateName == next) return;
                _templateName = next;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TemplateState));
            }
        }

        public string TemplateContent
        {
            get => _templateContent;
            set
            {
                string next = value ?? string.Empty;
                if (_templateContent == next) return;
                _templateContent = next;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TemplateState));
                RefreshCustomVariables();
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

        public string TemplateState => _isTemplateDraft
            ? "● Nouveau modèle non enregistré"
            : _editingTemplateId is null
                ? "Modèle intégré — lecture seule"
                : IsTemplateModified
                    ? "● Modèle modifié"
                    : "✓ Modèle enregistré";

        private bool IsTemplateModified =>
            _isTemplateDraft ||
            (_editingTemplateId is not null &&
             (!string.Equals(TemplateName, _savedTemplateName, StringComparison.Ordinal) ||
              !string.Equals(TemplateContent, _savedTemplateContent, StringComparison.Ordinal)));

        public ICommand NewTemplateCommand { get; }
        public ICommand SaveTemplateCommand { get; }
        public ICommand DeleteTemplateCommand { get; }
        public ICommand PreviewTemplateCommand { get; }
        public ICommand ImportTemplateCommand { get; }
        public ICommand ExportTemplateCommand { get; }

        private bool ConfirmDiscardTemplateChanges(string action)
        {
            if (!IsTemplateModified) return true;
            return MessageBox.Show(
                $"{action} abandonnera les modifications du modèle non enregistrées. Continuer ?",
                "NFOForge — Modèle non enregistré",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        private BBCodeTemplate? FindCustomTemplate(string name) =>
            _customTemplates.FirstOrDefault(t =>
                string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

        private void LoadTemplateEditor(string name)
        {
            BBCodeTemplate? template = FindCustomTemplate(name);
            _editingTemplateId = template?.Id;
            _isTemplateDraft = false;
            CustomVariables.Clear();
            TemplateName = template?.Name ?? string.Empty;
            TemplateContent = template?.Content ?? string.Empty;
            _savedTemplateName = TemplateName;
            _savedTemplateContent = TemplateContent;
            TemplatePreview = string.Empty;
            OnPropertyChanged(nameof(TemplateState));
        }

        private void LoadCustomTemplates()
        {
            try
            {
                var loaded = _templateStorage.Load();
                _customTemplates.Clear();
                foreach (BBCodeTemplate template in loaded)
                    _customTemplates.Add(template);
                foreach (BBCodeTemplate template in loaded)
                    AvailableTemplates.Add(template.Name);
            }
            catch (Exception ex) when (IsTemplateStorageError(ex))
            {
                StatusMessage = "Chargement des modèles impossible : " + ex.Message;
            }
        }

        private static bool IsTemplateStorageError(Exception ex) =>
            ex is IOException or UnauthorizedAccessException or ArgumentException
                or InvalidOperationException or JsonException
                or System.Security.SecurityException or RegexMatchTimeoutException;

        private void NewTemplate()
        {
            if (!ConfirmDiscardTemplateChanges("Créer un modèle")) return;
            _editingTemplateId = null;
            _isTemplateDraft = true;
            CustomVariables.Clear();
            TemplateName = string.Empty;
            TemplateContent = "[b]{Nom}[/b]\nVersion : {Version}\n\n{Description}";
            TemplatePreview = string.Empty;
            OnPropertyChanged(nameof(TemplateState));
            StatusMessage = "Nouveau modèle : complétez le nom et le contenu, puis enregistrez.";
        }

        private void SaveTemplate()
        {
            string name = TemplateName?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(name))
            {
                StatusMessage =
                    "Erreur : renseignez un nom pour le modèle BBCode.";
                return;
            }

            if (name.Length > 100)
            {
                StatusMessage =
                    "Erreur : le nom du modèle ne peut pas dépasser 100 caractères.";
                return;
            }
            if (!_isTemplateDraft && _editingTemplateId is null)
            {
                StatusMessage = "Cliquez sur Nouveau pour créer un modèle personnalisé.";
                return;
            }

            try
            {
                string content = TemplateContent;
                if (_isTemplateDraft)
                {
                    BBCodeTemplate created = _templateStorage.Create(name, content);
                    _customTemplates.Add(created);
                    AvailableTemplates.Add(created.Name);
                    _isTemplateDraft = false;
                    _editingTemplateId = created.Id;
                    _savedTemplateName = created.Name;
                    _savedTemplateContent = created.Content;
                    TemplateName = created.Name;
                    SelectedTemplate = created.Name;
                }
                else
                {
                    BBCodeTemplate? existing = _customTemplates.FirstOrDefault(
                        t => t.Id == _editingTemplateId);
                    if (existing is null)
                        throw new InvalidOperationException("Modèle introuvable. Rechargez NFOForge.");
                    string previousName = existing.Name;
                    var updated = new BBCodeTemplate
                    {
                        Id = existing.Id,
                        Name = name,
                        Content = content,
                        IsBuiltIn = false
                    };
                    _templateStorage.Update(updated);
                    existing.Name = name;
                    existing.Content = content;
                    _savedTemplateName = name;
                    _savedTemplateContent = content;
                    TemplateName = name;
                    int index = AvailableTemplates.IndexOf(previousName);
                    if (index >= 0) AvailableTemplates[index] = name;
                    SelectedTemplate = name;
                }
                OnPropertyChanged(nameof(TemplateState));
                StatusMessage = "Modèle BBCode enregistré.";
            }
            catch (Exception ex) when (
                ex is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or InvalidOperationException
                or InvalidDataException
                or System.Security.SecurityException)
            {
                StatusMessage =
                    "Impossible d'enregistrer le modèle : " + ex.Message;
            }
        }

        private void DeleteTemplate()
        {
            if (_isTemplateDraft)
            {
                StatusMessage = "Ce brouillon n'est pas enregistré. Utilisez Nouveau pour repartir.";
                return;
            }
            BBCodeTemplate? template = _customTemplates.FirstOrDefault(t => t.Id == _editingTemplateId);
            if (template is null)
            {
                StatusMessage = "Les modèles intégrés ne peuvent pas être supprimés.";
                return;
            }
            if (!ConfirmDiscardTemplateChanges("Supprimer ce modèle")) return;
            if (MessageBox.Show($"Supprimer définitivement « {template.Name} » ?",
                    "NFOForge — Suppression de modèle", MessageBoxButton.YesNo,
                    MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
            try
            {
                _templateStorage.Delete(template.Id);
                _customTemplates.Remove(template);
                AvailableTemplates.Remove(template.Name);
                _editingTemplateId = null;
                _isTemplateDraft = false;
                SelectedTemplate = "Classique";
                LoadTemplateEditor("Classique");
                StatusMessage = "Modèle personnalisé supprimé.";
            }
            catch (Exception ex) when (IsTemplateStorageError(ex))
            {
                StatusMessage = "Suppression impossible : " + ex.Message;
            }
        }

        private void PreviewTemplate()
        {
            try
            {
                var template = new BBCodeTemplate { Name = TemplateName, Content = TemplateContent };
                string text = _templateService.Render(
                    template, _metadata, PosterUrl, ScreenshotUrl, GetCustomValues());
                // Le résultat est du BBCode brut : aucune action réseau.
                TemplatePreview = text;
                StatusMessage = "Prévisualisation du modèle actualisée.";
            }
            catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
            {
                TemplatePreview = string.Empty;
                StatusMessage = "Prévisualisation du modèle impossible : " + ex.Message;
            }
        }


        private void ImportTemplate()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Importer un modèle BBCode JSON",
                Filter = "Modèles JSON (*.json)|*.json",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog() != true) return;
            if (!ConfirmDiscardTemplateChanges("Importer un modèle")) return;

            try
            {
                // Validation, contrôle des doublons et écriture sécurisée dans le service.
                BBCodeTemplate imported = _templateStorage.ImportFromFile(dialog.FileName);
                _customTemplates.Add(imported);
                AvailableTemplates.Add(imported.Name);
                // Changement contrôlé : la confirmation a déjà eu lieu avant l'import.
                _selectedTemplate = imported.Name;
                OnPropertyChanged(nameof(SelectedTemplate));
                LoadTemplateEditor(imported.Name);
                StatusMessage = "Modèle BBCode importé : " + imported.Name;
            }
            catch (Exception ex) when (IsTemplateStorageError(ex))
            {
                StatusMessage = "Importation du modèle impossible : " + ex.Message;
            }
        }

        private void ExportTemplate()
        {
            if (_isTemplateDraft || _editingTemplateId is null)
            {
                StatusMessage = "Enregistrez d'abord un modèle personnalisé pour l'exporter.";
                return;
            }
            if (IsTemplateModified)
            {
                StatusMessage = "Enregistrez les modifications du modèle avant son exportation.";
                return;
            }
            BBCodeTemplate? selected = _customTemplates.FirstOrDefault(t => t.Id == _editingTemplateId);
            if (selected is null)
            {
                StatusMessage = "Modèle personnalisé introuvable.";
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Exporter un modèle BBCode JSON",
                Filter = "Modèles JSON (*.json)|*.json",
                DefaultExt = ".json",
                AddExtension = true,
                FileName = "NFOForge_Modele_BBCode.json",
                OverwritePrompt = true
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                _templateStorage.ExportToFile(selected.Id, dialog.FileName);
                StatusMessage = "Modèle BBCode exporté : " + Path.GetFileName(dialog.FileName);
            }
            catch (Exception ex) when (IsTemplateStorageError(ex))
            {
                StatusMessage = "Exportation du modèle impossible : " + ex.Message;
            }
        }

        public string BBCodeText
        {
            get => _bbCodeText;
            set
            {
                string next = value ?? string.Empty;
                if (_bbCodeText == next) return;
                _bbCodeText = next;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CharacterCount));
                OnPropertyChanged(nameof(LineCount));
                OnPropertyChanged(nameof(DocumentState));
                ValidationMessage = "Validation non effectuée.";
                _previewTimer.Stop();
                _previewTimer.Start();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set { if (_statusMessage == value) return; _statusMessage = value; OnPropertyChanged(); }
        }

        public string ValidationMessage
        {
            get => _validationMessage;
            private set { if (_validationMessage == value) return; _validationMessage = value; OnPropertyChanged(); }
        }

        public int CharacterCount => BBCodeText.Length;
        public int LineCount => BBCodeText.Length == 0 ? 0 : BBCodeText.Split('\n').Length;
        public string DocumentState => BBCodeText == _savedText ? "✓ Enregistré" : "● Modifié";

        public ICommand GenerateCommand { get; }
        public ICommand ImportCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand CopyCommand { get; }
        public ICommand ValidateCommand { get; }
        public ICommand InsertBoldCommand { get; }
        public ICommand InsertItalicCommand { get; }
        public ICommand InsertColorCommand { get; }
        public ICommand InsertUrlCommand { get; }
        public ICommand InsertImageCommand { get; }

        public BBCodeEditorViewModel(SoftwareMetadata metadata)
        {
            _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _previewTimer.Tick += (_, _) =>
            {
                _previewTimer.Stop();
                UpdatePreview();
            };
            GenerateCommand = new RelayCommand(_ => Generate());
            ImportCommand = new RelayCommand(_ => Import());
            ExportCommand = new RelayCommand(_ => Export());
            CopyCommand = new RelayCommand(_ => Copy());
            ValidateCommand = new RelayCommand(_ => Validate());
            InsertBoldCommand = new RelayCommand(_ => AppendTag("[b]Texte en gras[/b]"));
            InsertItalicCommand = new RelayCommand(_ => AppendTag("[i]Texte en italique[/i]"));
            InsertColorCommand = new RelayCommand(_ => AppendTag("[color=#00C8FF]Texte coloré[/color]"));
            InsertUrlCommand = new RelayCommand(_ => AppendTag("[url=https://exemple.org]Lien[/url]"));
            InsertImageCommand = new RelayCommand(_ => AppendTag("[img]https://exemple.org/image.png[/img]"));
            NewTemplateCommand = new RelayCommand(_ => NewTemplate());
            SaveTemplateCommand = new RelayCommand(_ => SaveTemplate());
            DeleteTemplateCommand = new RelayCommand(_ => DeleteTemplate());
            PreviewTemplateCommand = new RelayCommand(_ => PreviewTemplate());
            ImportTemplateCommand = new RelayCommand(_ => ImportTemplate());
            ExportTemplateCommand = new RelayCommand(_ => ExportTemplate());
            LoadCustomTemplates();
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            try
            {
                PreviewDocument = _renderService.Render(BBCodeText);
            }
            catch (RegexMatchTimeoutException)
            {
                StatusMessage = "Prévisualisation interrompue : délai de traitement dépassé.";
            }
            catch (ArgumentException ex)
            {
                StatusMessage = "Prévisualisation indisponible : " + ex.Message;
            }
        }

        private bool ConfirmDiscard(string action) =>
            BBCodeText == _savedText || MessageBox.Show(
                $"{action} remplacera les modifications non enregistrées. Continuer ?",
                "NFOForge — Confirmation", MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

        private void MarkSaved()
        {
            _savedText = BBCodeText;
            OnPropertyChanged(nameof(DocumentState));
        }

        private static void Field(StringBuilder sb, string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                sb.AppendLine($"[b]{label} :[/b] {value.Trim()}");
        }

        private void Generate()
        {
            if (string.IsNullOrWhiteSpace(_metadata.Name))
            {
                StatusMessage = "Renseignez le nom dans Métadonnées.";
                return;
            }
            if (!ConfirmDiscard("La génération")) return;

            BBCodeTemplate? custom = FindCustomTemplate(SelectedTemplate);
            if (custom is not null)
            {
                try
                {
                    BBCodeText = _templateService.Render(
                        custom, _metadata, PosterUrl, ScreenshotUrl, GetCustomValues());
                    StatusMessage = $"BBCode généré avec le modèle {custom.Name}.";
                }
                catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
                {
                    StatusMessage = "Génération impossible : " + ex.Message;
                }
                return;
            }

            var sb = new StringBuilder();
            if (SelectedTemplate == "Moderne")
            {
                sb.AppendLine("[center][size=20][color=#00C8FF][b]" + _metadata.Name.Trim() + "[/b][/color][/size][/center]");
                sb.AppendLine("[center][i]Présentation réalisée avec NFOForge[/i][/center]");
            }
            else
            {
                sb.AppendLine("[b][size=18]" + _metadata.Name.Trim() + "[/size][/b]");
            }
            sb.AppendLine();
            Field(sb, "Nom", _metadata.Name);
            Field(sb, "Version", _metadata.Version);
            Field(sb, "Éditeur", _metadata.Publisher);
            Field(sb, "Développeur", _metadata.Developer);
            Field(sb, "Catégorie", _metadata.Category);
            Field(sb, "Sous-catégorie", _metadata.Subcategory);
            Field(sb, "Système", _metadata.OperatingSystem);
            Field(sb, "Architecture", _metadata.Architecture);
            Field(sb, "Langue", _metadata.Language);
            if (_metadata.ReleaseDate.HasValue)
                Field(sb, "Date de sortie", _metadata.ReleaseDate.Value.ToString("dd/MM/yyyy"));
            if (!string.IsNullOrWhiteSpace(_metadata.Description))
                sb.AppendLine("\n[b]DESCRIPTION[/b]\n" + _metadata.Description.Trim());
            if (!string.IsNullOrWhiteSpace(_metadata.Features))
                sb.AppendLine("\n[b]FONCTIONNALITÉS[/b]\n" + _metadata.Features.Trim());
            if (!string.IsNullOrWhiteSpace(_metadata.AdditionalInformation))
                sb.AppendLine("\n[b]INFORMATIONS COMPLÉMENTAIRES[/b]\n" + _metadata.AdditionalInformation.Trim());

            BBCodeText = sb.ToString();
            StatusMessage = "BBCode généré depuis les métadonnées.";
        }

        private void AppendTag(string snippet)
        {
            BBCodeText += (BBCodeText.Length == 0 || BBCodeText.EndsWith('\n') ? "" : Environment.NewLine) + snippet;
            StatusMessage = "Balise ajoutée en fin de document.";
        }

        private void Import()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Importer un BBCode",
                Filter = "Fichiers texte (*.txt)|*.txt|Fichiers BBCode (*.bbcode)|*.bbcode|Tous les fichiers (*.*)|*.*",
                CheckFileExists = true
            };
            if (dialog.ShowDialog() != true) return;
            try
            {
                using var stream = new FileStream(dialog.FileName, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length > MaxImportBytes)
                {
                    StatusMessage = "Import refusé : fichier supérieur à 2 Mo.";
                    return;
                }
                using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
                string text = reader.ReadToEnd();
                if (!ConfirmDiscard("L'importation")) return;
                BBCodeText = text;
                MarkSaved();
                StatusMessage = "BBCode importé : " + Path.GetFileName(dialog.FileName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or DecoderFallbackException or System.Security.SecurityException)
            {
                StatusMessage = "Erreur d'importation : " + ex.Message;
            }
        }

        private void Export()
        {
            if (string.IsNullOrWhiteSpace(BBCodeText))
            {
                StatusMessage = "Le BBCode est vide.";
                return;
            }
            var dialog = new SaveFileDialog
            {
                Title = "Exporter le BBCode",
                Filter = "Fichiers texte (*.txt)|*.txt",
                DefaultExt = ".txt",
                AddExtension = true,
                FileName = "NFOForge_BBCode.txt",
                OverwritePrompt = true
            };
            if (dialog.ShowDialog() != true) return;
            try
            {
                byte[] bytes = new UTF8Encoding(false, true).GetBytes(BBCodeText);
                _saveService.Save(dialog.FileName, bytes);
                MarkSaved();
                StatusMessage = "BBCode exporté en UTF-8 sans BOM : " + Path.GetFileName(dialog.FileName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or EncoderFallbackException or System.Security.SecurityException)
            {
                StatusMessage = "Erreur d'exportation : " + ex.Message;
            }
        }

        private void Copy()
        {
            if (BBCodeText.Length == 0) { StatusMessage = "Aucun texte à copier."; return; }
            try
            {
                Clipboard.SetText(BBCodeText);
                StatusMessage = "BBCode copié dans le presse-papiers.";
            }
            catch (System.Runtime.InteropServices.ExternalException ex)
            {
                StatusMessage = "Copie impossible : " + ex.Message;
            }
        }

        private void Validate()
        {
            try
            {
                ValidationMessage = _validationService.Validate(BBCodeText);
            }
            catch (RegexMatchTimeoutException)
            {
                ValidationMessage = "Validation interrompue : délai de traitement dépassé.";
            }
        }

        // Étape 27.2 : extraction et restauration du contenu propre au projet.
        public bool HasUnsavedTemplateChanges => IsTemplateModified;

        public Dictionary<string, string> CaptureCustomValues() =>
            CustomVariables.ToDictionary(item => item.Name, item => item.Value,
                StringComparer.OrdinalIgnoreCase);

        public void RestoreProjectState(
            string text, string templateName,
            IReadOnlyDictionary<string, string> customValues,
            string posterUrl, string screenshotUrl)
        {
            ArgumentNullException.ThrowIfNull(customValues);
            string effectiveTemplate = AvailableTemplates.Contains(templateName)
                ? templateName : "Classique";

            // Affectation directe : aucun popup secondaire après validation centrale.
            _selectedTemplate = effectiveTemplate;
            OnPropertyChanged(nameof(SelectedTemplate));
            LoadTemplateEditor(effectiveTemplate);

            foreach (BBCodeCustomVariable variable in CustomVariables)
                if (customValues.TryGetValue(variable.Name, out string? value))
                    variable.Value = value ?? string.Empty;

            PosterUrl = posterUrl ?? string.Empty;
            ScreenshotUrl = screenshotUrl ?? string.Empty;
            BBCodeText = text ?? string.Empty;
            _savedText = BBCodeText;
            OnPropertyChanged(nameof(DocumentState));
            StatusMessage = effectiveTemplate == templateName
                ? "BBCode restauré depuis le projet."
                : $"Modèle BBCode « {templateName} » absent : modèle Classique sélectionné.";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    // Un champ = une variable découverte automatiquement dans le modèle.
    public sealed class BBCodeCustomVariable : INotifyPropertyChanged
    {
        public BBCodeCustomVariable(string name, string value)
        {
            Name = name;
            _value = value;
        }

        public string Name { get; }
        private string _value;
        public string Value
        {
            get => _value;
            set
            {
                string next = value ?? string.Empty;
                if (_value == next) return;
                _value = next;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

}