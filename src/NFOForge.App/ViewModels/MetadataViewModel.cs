using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NFOForge.App.Models;
using System.Collections.ObjectModel;
using System.Windows.Input;
using NFOForge.App.Commands;
using NFOForge.App.Services;
using System.Collections.Generic;

namespace NFOForge.App.ViewModels
{
    public class MetadataViewModel : INotifyPropertyChanged
    {
        private readonly SoftwareMetadata _metadata;


        public IReadOnlyList<string> AvailableCategories { get; } =
        [
            "Applications",
            "Jeux",
            "Films",
            "Séries",
            "Musique"
        ];

        public ObservableCollection<string> AvailableSubcategories { get; } = new();

        public ObservableCollection<string> AvailableProfiles { get; } = new();

        private string _profileInformation = string.Empty;

        public string ProfileInformation
        {
            get => _profileInformation;
            private set
            {
                if (_profileInformation == value)
                    return;

                _profileInformation = value;
                OnPropertyChanged();
            }
        }


        private readonly MetadataValidationService _validationService = new();

        public ObservableCollection<string> ValidationErrors { get; } = new();

        public ICommand ValidateCommand { get; }

        private int _completionPercentage;

        public int CompletionPercentage
        {
            get => _completionPercentage;
            private set
            {
                if (_completionPercentage == value)
                    return;

                _completionPercentage = value;
                OnPropertyChanged();
            }
        }

        private string _validationStatus = "Non vérifié";

        public string ValidationStatus
        {
            get => _validationStatus;
            private set
            {
                if (_validationStatus == value)
                    return;

                _validationStatus = value;
                OnPropertyChanged();
            }
        }


        public MetadataViewModel(SoftwareMetadata metadata)
        {
            _metadata = metadata
                ?? throw new ArgumentNullException(nameof(metadata));

            ValidateCommand = new RelayCommand(_ => Validate());

            // Initialiser les options correspondant à la catégorie.
            UpdateCategoryOptions();

            // Calculer la complétude après initialisation.
            Validate();
        }


        public SoftwareMetadata Metadata => _metadata;

        public string Name
        {
            get => _metadata.Name;
            set => SetValue(_metadata.Name, value,
                v => _metadata.Name = v);
        }

        public string Version
        {
            get => _metadata.Version;
            set => SetValue(_metadata.Version, value,
                v => _metadata.Version = v);
        }

        public string Publisher
        {
            get => _metadata.Publisher;
            set => SetValue(_metadata.Publisher, value,
                v => _metadata.Publisher = v);
        }

        public string Developer
        {
            get => _metadata.Developer;
            set => SetValue(_metadata.Developer, value,
                v => _metadata.Developer = v);
        }


        private void UpdateCategoryOptions()
        {
            AvailableSubcategories.Clear();
            AvailableProfiles.Clear();

            if (string.Equals(
                _metadata.Category,
                "Applications",
                StringComparison.OrdinalIgnoreCase))
            {
                AvailableSubcategories.Add("Logiciels");
                AvailableProfiles.Add("C411");

                if (_metadata.Subcategory != "Logiciels")
                {
                    _metadata.Subcategory = "Logiciels";
                    OnPropertyChanged(nameof(Subcategory));
                }

                if (_metadata.PublicationProfile != "C411")
                {
                    _metadata.PublicationProfile = "C411";
                    OnPropertyChanged(nameof(PublicationProfile));
                }

                ProfileInformation =
                    "Référentiel C411 disponible pour Applications / Logiciels.";
            }
            else
            {
                if (!string.IsNullOrEmpty(_metadata.Subcategory))
                {
                    _metadata.Subcategory = string.Empty;
                    OnPropertyChanged(nameof(Subcategory));
                }

                if (!string.IsNullOrEmpty(_metadata.PublicationProfile))
                {
                    _metadata.PublicationProfile = string.Empty;
                    OnPropertyChanged(nameof(PublicationProfile));
                }

                ProfileInformation =
                    "Aucun référentiel de conformité disponible pour cette catégorie. " +
                    "La saisie des métadonnées reste autorisée.";
            }
        }

        public string Category
        {
            get => _metadata.Category;

            set
            {
                string newValue = value ?? string.Empty;

                if (_metadata.Category == newValue)
                    return;

                _metadata.Category = newValue;
                OnPropertyChanged();

                UpdateCategoryOptions();
                Validate();
            }
        }


        public string Subcategory
        {
            get => _metadata.Subcategory;
            set => SetValue(_metadata.Subcategory, value,
                v => _metadata.Subcategory = v);
        }

        public string PublicationProfile
        {
            get => _metadata.PublicationProfile;
            set => SetValue(_metadata.PublicationProfile, value,
                v => _metadata.PublicationProfile = v);
        }

        public string OperatingSystem
        {
            get => _metadata.OperatingSystem;
            set => SetValue(_metadata.OperatingSystem, value,
                v => _metadata.OperatingSystem = v);
        }

        public string Architecture
        {
            get => _metadata.Architecture;
            set => SetValue(_metadata.Architecture, value,
                v => _metadata.Architecture = v);
        }

        public string Language
        {
            get => _metadata.Language;
            set => SetValue(_metadata.Language, value,
                v => _metadata.Language = v);
        }

        public DateTime? ReleaseDate
        {
            get => _metadata.ReleaseDate;
            set
            {
                if (_metadata.ReleaseDate == value)
                    return;

                _metadata.ReleaseDate = value;
                OnPropertyChanged();
            }
        }

        public string Description
        {
            get => _metadata.Description;
            set => SetValue(_metadata.Description, value,
                v => _metadata.Description = v);
        }

        public string Features
        {
            get => _metadata.Features;
            set => SetValue(_metadata.Features, value,
                v => _metadata.Features = v);
        }

        public string AdditionalInformation
        {
            get => _metadata.AdditionalInformation;
            set => SetValue(_metadata.AdditionalInformation, value,
                v => _metadata.AdditionalInformation = v);
        }

        private void SetValue(
    string currentValue,
    string? newValue,
    Action<string> setter,
    [CallerMemberName] string? propertyName = null)
        {
            string normalizedValue = newValue ?? string.Empty;

            if (currentValue == normalizedValue)
                return;

            setter(normalizedValue);

            OnPropertyChanged(propertyName);

            Validate();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Validate()
        {
            var errors = _validationService.Validate(_metadata);

            // Actualisation des messages de validation.
            ValidationErrors.Clear();

            foreach (string error in errors)
            {
                ValidationErrors.Add(error);
            }

            // Le référentiel C411 est disponible uniquement
            // pour la catégorie Applications.
            bool isApplications = string.Equals(
                _metadata.Category,
                "Applications",
                StringComparison.OrdinalIgnoreCase);

            // Sept champs communs à toutes les catégories.
            // Deux champs supplémentaires pour Applications.
            int totalRequiredFields = isApplications ? 9 : 7;

            int completedFields = 0;

            // Identification : 2 champs.
            if (!string.IsNullOrWhiteSpace(_metadata.Name))
                completedFields++;

            if (!string.IsNullOrWhiteSpace(_metadata.Version))
                completedFields++;

            // Classification : 1 champ commun.
            if (!string.IsNullOrWhiteSpace(_metadata.Category))
                completedFields++;

            // Classification C411 : 2 champs supplémentaires.
            if (isApplications)
            {
                if (string.Equals(
                    _metadata.Subcategory,
                    "Logiciels",
                    StringComparison.OrdinalIgnoreCase))
                {
                    completedFields++;
                }

                if (string.Equals(
                    _metadata.PublicationProfile,
                    "C411",
                    StringComparison.OrdinalIgnoreCase))
                {
                    completedFields++;
                }
            }

            // Informations techniques : 3 champs.
            if (!string.IsNullOrWhiteSpace(_metadata.OperatingSystem))
                completedFields++;

            if (!string.IsNullOrWhiteSpace(_metadata.Architecture))
                completedFields++;

            if (!string.IsNullOrWhiteSpace(_metadata.Language))
                completedFields++;

            // Présentation : 1 champ.
            if (!string.IsNullOrWhiteSpace(_metadata.Description))
                completedFields++;

            CompletionPercentage = (int)Math.Round(
                completedFields * 100.0 / totalRequiredFields);

            ValidationStatus = errors.Count == 0
                ? "Informations obligatoires complètes"
                : $"{errors.Count} anomalie(s) de validation";
        }

        protected void OnPropertyChanged(
            [CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));
        }
    }
}