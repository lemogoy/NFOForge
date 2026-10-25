using System;
using System.Collections.Generic;

namespace NFOForge.App.Models
{
    /// <summary>
    /// Format persistant d'un projet NFOForge. Les DTO sont indépendants des ViewModels WPF.
    /// </summary>
    public sealed class NFOForgeProject
    {
        public int FormatVersion { get; set; } = 1;
        public string ProjectId { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;

        public ProjectMetadata Metadata { get; set; } = new();
        public string NfoText { get; set; } = string.Empty;
        public string NfoEncoding { get; set; } = "UTF-8 sans BOM";
        public string NfoTemplate { get; set; } = "Classique";
        public string BBCodeText { get; set; } = string.Empty;
        public string BBCodeTemplate { get; set; } = "Classique";
        public Dictionary<string, string> BBCodeCustomValues { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
        public string PosterUrl { get; set; } = string.Empty;
        public string ScreenshotUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// Instantané des métadonnées, sans liaison WPF ni notifications de changement.
    /// </summary>
    public sealed class ProjectMetadata
    {
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Publisher { get; set; } = string.Empty;
        public string Developer { get; set; } = string.Empty;
        public string Category { get; set; } = "Applications";
        public string Subcategory { get; set; } = "Logiciels";
        public string PublicationProfile { get; set; } = "C411";
        public string OperatingSystem { get; set; } = string.Empty;
        public string Architecture { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public DateTime? ReleaseDate { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Features { get; set; } = string.Empty;
        public string AdditionalInformation { get; set; } = string.Empty;

        public static ProjectMetadata From(SoftwareMetadata metadata)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            return new ProjectMetadata
            {
                Name = metadata.Name,
                Version = metadata.Version,
                Publisher = metadata.Publisher,
                Developer = metadata.Developer,
                Category = metadata.Category,
                Subcategory = metadata.Subcategory,
                PublicationProfile = metadata.PublicationProfile,
                OperatingSystem = metadata.OperatingSystem,
                Architecture = metadata.Architecture,
                Language = metadata.Language,
                ReleaseDate = metadata.ReleaseDate,
                Description = metadata.Description,
                Features = metadata.Features,
                AdditionalInformation = metadata.AdditionalInformation
            };
        }

        /// <summary>Restauration sur l'instance partagée entre les écrans.</summary>
        public void ApplyTo(SoftwareMetadata metadata)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            metadata.Name = Name;
            metadata.Version = Version;
            metadata.Publisher = Publisher;
            metadata.Developer = Developer;
            metadata.Category = Category;
            metadata.Subcategory = Subcategory;
            metadata.PublicationProfile = PublicationProfile;
            metadata.OperatingSystem = OperatingSystem;
            metadata.Architecture = Architecture;
            metadata.Language = Language;
            metadata.ReleaseDate = ReleaseDate;
            metadata.Description = Description;
            metadata.Features = Features;
            metadata.AdditionalInformation = AdditionalInformation;
        }
    }
}