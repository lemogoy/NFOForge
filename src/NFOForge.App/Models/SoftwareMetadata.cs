using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NFOForge.App.Models
{
    public class SoftwareMetadata : INotifyPropertyChanged
    {
        // Identification
        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set => SetValue(ref _name, value);
        }
        private string _version = string.Empty;
        public string Version
        {
            get => _version;
            set => SetValue(ref _version, value);
        }
        private string _publisher = string.Empty;
        public string Publisher
        {
            get => _publisher;
            set => SetValue(ref _publisher, value);
        }
        private string _developer = string.Empty;
        public string Developer
        {
            get => _developer;
            set => SetValue(ref _developer, value);
        }

        // Classification
        private string _category = "Applications";
        public string Category
        {
            get => _category;
            set => SetValue(ref _category, value);
        }
        private string _subcategory = "Logiciels";
        public string Subcategory
        {
            get => _subcategory;
            set => SetValue(ref _subcategory, value);
        }
        private string _publicationProfile = "C411";
        public string PublicationProfile
        {
            get => _publicationProfile;
            set => SetValue(ref _publicationProfile, value);
        }

        // Informations techniques
        private string _operatingSystem = string.Empty;
        public string OperatingSystem
        {
            get => _operatingSystem;
            set => SetValue(ref _operatingSystem, value);
        }
        private string _architecture = string.Empty;
        public string Architecture
        {
            get => _architecture;
            set => SetValue(ref _architecture, value);
        }
        private string _language = string.Empty;
        public string Language
        {
            get => _language;
            set => SetValue(ref _language, value);
        }
        private DateTime? _releaseDate;
        public DateTime? ReleaseDate
        {
            get => _releaseDate;
            set => SetValue(ref _releaseDate, value);
        }

        // Présentation
        private string _description = string.Empty;
        public string Description
        {
            get => _description;
            set => SetValue(ref _description, value);
        }
        private string _features = string.Empty;
        public string Features
        {
            get => _features;
            set => SetValue(ref _features, value);
        }
        private string _additionalInformation = string.Empty;
        public string AdditionalInformation
        {
            get => _additionalInformation;
            set => SetValue(ref _additionalInformation, value);
        }
        public event PropertyChangedEventHandler? PropertyChanged;

        private void SetValue<T>(ref T field, T value,
            [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}