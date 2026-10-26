using NFOForge.App.Services;
using NFOForge.App.ViewModels;
using NFOForge.App.Views;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Windows;


namespace NFOForge.App
{
    public partial class MainWindow : Window
    {
        // Client HTTP partagé pour les vérifications.
        // Les redirections automatiques sont désactivées.
        private static readonly HttpClient UpdateHttpClient =
            new(new HttpClientHandler
            {
                AllowAutoRedirect = false
            });

        private readonly CancellationTokenSource _updateCancellation = new();

        private bool _updateCheckStarted;
        private bool _isClosing;

        public MainWindow()

        {
            InitializeComponent();

            DataContext = new MainViewModel();
        }

        

        /// <summary>
        /// Déclenche la vérification des mises à jour
        /// après le chargement de la fenêtre principale.
        ///
        /// Aucun serveur n'est contacté si l'URL
        /// du manifeste n'est pas configurée.
        /// </summary>
        private async void Window_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            // Une seule vérification automatique par ouverture.
            if (_updateCheckStarted)
                return;

            _updateCheckStarted = true;


            // Respecter le choix enregistré par l'utilisateur.
            var preferencesService = new UpdatePreferencesService();

            if (!preferencesService.Load().CheckOnStartup)
                return;



            Uri manifestUri = new(
                "https://guidessence.fr/uploads/program/nfoforge/update/stable.json",
                UriKind.Absolute);


            if (!Version.TryParse(
                    AppVersionInfo.Version,
                    out Version? installedVersion))
                return;

            try
            {
                var service = new UpdateService(
                    UpdateHttpClient,
                    manifestUri);

                UpdateCheckResult result =
                    await service.CheckAsync(
                        installedVersion,
                        _updateCancellation.Token);

                // Ne jamais afficher de notification
                // pendant la fermeture.
                if (_isClosing)
                    return;


                // Ne notifier qu'une mise à jour complète,
                // lorsque le contrôle automatique est autorisé.
                if (!UpdateNotificationPolicy.ShouldNotify(
                        result,
                        checkOnStartup: true,
                        isClosing: _isClosing))
                {
                    return;
                }

                ShowUpdateNotification(result.Update!);

            }
            catch (OperationCanceledException)
                when (_updateCancellation.IsCancellationRequested)
            {
                // Fermeture de la fenêtre : annulation normale.
            }
            catch (Exception ex)
            {
                // Une erreur de mise à jour ne doit jamais
                // empêcher l'utilisation de NFOForge.
                Debug.WriteLine(
                    "NFOForge - Vérification des mises à jour : "
                    + ex);
            }
        }

        /// <summary>
        /// Propose à l'utilisateur de consulter
        /// les informations de la nouvelle version.
        ///
        /// Aucun installateur n'est exécuté.
        /// </summary>

        private void ShowUpdateNotification(UpdateInfo update)
        {
            if (_isClosing)
                return;

            var dialog = new UpdateNotificationWindow(update)
            {
                Owner = this
            };

            dialog.ShowDialog();
        }

        /// <summary>
        /// Préserve la confirmation de fermeture existante.
        /// Annule la vérification uniquement si la fermeture
        /// du projet est effectivement autorisée.
        /// </summary>
        private void Window_Closing(
            object? sender,
            CancelEventArgs e)
        {
            if (DataContext is MainViewModel viewModel &&
                !viewModel.ConfirmCloseProject())
            {
                e.Cancel = true;
                return;
            }

            _isClosing = true;

            _updateCancellation.Cancel();
        }
    }
}