
using System;
using System.ComponentModel;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using NFOForge.App.Commands;
using NFOForge.App.Services;

namespace NFOForge.App.ViewModels
{
    public sealed class UpdateSettingsViewModel : INotifyPropertyChanged
    {
        private readonly UpdatePreferencesService _preferencesService = new();
        private bool _checkOnStartup;
        public bool CheckOnStartup
        {
            get => _checkOnStartup;

            set
            {
                if (_checkOnStartup == value)
                    return;

                try
                {
                    _preferencesService.Save(new UpdatePreferences
                    {
                        CheckOnStartup = value
                    });

                    _checkOnStartup = value;
                    Notify();
                }
                catch (Exception ex) when (
                    ex is System.IO.IOException
                    or UnauthorizedAccessException)
                {
                    Status = "Impossible d'enregistrer la préférence : "
                        + ex.Message;
                }
            }
        }

        private static readonly Uri ManifestUri = new(
            "https://guidessence.fr/uploads/program/nfoforge/update/stable.json");

        private static readonly HttpClient Client = new(
            new HttpClientHandler
            {
                AllowAutoRedirect = false
            });

        private bool _isChecking;
        private string _status = "Aucune vérification manuelle effectuée.";
        private string _publishedVersion = "Non vérifiée";
        private string _lastCheck = "Jamais";

        public event PropertyChangedEventHandler? PropertyChanged;

        public string InstalledVersion => AppVersionInfo.Version;

        public string PublishedVersion
        {
            get => _publishedVersion;
            private set
            {
                _publishedVersion = value;
                Notify();
            }
        }

        public string Status
        {
            get => _status;
            private set
            {
                _status = value;
                Notify();
            }
        }

        public string LastCheck
        {
            get => _lastCheck;
            private set
            {
                _lastCheck = value;
                Notify();
            }
        }

        public bool IsChecking
        {
            get => _isChecking;
            private set
            {
                _isChecking = value;
                Notify();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public ICommand CheckUpdatesCommand { get; }

        public UpdateSettingsViewModel()
        {
            CheckUpdatesCommand = new RelayCommand(
                _ => _ = CheckUpdatesAsync(),
                _ => !IsChecking);
            _checkOnStartup = _preferencesService.Load().CheckOnStartup;

            CheckUpdatesCommand = new RelayCommand(
                _ => _ = CheckUpdatesAsync(),
                _ => !IsChecking);
        }

        private async Task CheckUpdatesAsync()
        {
            if (IsChecking)
                return;

            IsChecking = true;
            Status = "Recherche des mises à jour en cours...";

            try
            {
                if (!Version.TryParse(
                    InstalledVersion,
                    out Version? installed))
                {
                    Status = "Version locale non reconnue.";
                    return;
                }

                var service = new UpdateService(Client, ManifestUri);

                UpdateCheckResult result =
                    await service.CheckAsync(installed);

                LastCheck = DateTime.Now.ToString("dd/MM/yyyy HH:mm");

                switch (result.Status)
                {
                    case UpdateCheckStatus.UpToDate:
                        PublishedVersion = "Aucune version plus récente";
                        Status = "Votre version est à jour.";
                        break;

                    case UpdateCheckStatus.UpdateAvailable:
                        PublishedVersion =
                            result.Update?.Version.ToString(3)
                            ?? "Inconnue";

                        Status = "Une nouvelle version est disponible.";
                        break;

                    default:
                        PublishedVersion = "Indisponible";
                        Status = "Impossible de vérifier les mises à jour.";
                        break;
                }
            }
            catch (Exception)
            {
                PublishedVersion = "Indisponible";
                Status = "Erreur pendant la vérification.";
            }
            finally
            {
                IsChecking = false;
            }
        }

        private void Notify(
            [CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(name));
        }
    }
}
