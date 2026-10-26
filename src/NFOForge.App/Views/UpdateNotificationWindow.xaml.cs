
using System;
using System.Diagnostics;
using System.Windows;
using NFOForge.App.Services;

namespace NFOForge.App.Views
{
    public partial class UpdateNotificationWindow : Window
    {
        private readonly UpdateInfo _update;

        public string InstalledVersion { get; }

        public string AvailableVersion { get; }

        public UpdateNotificationWindow(UpdateInfo update)
        {
            ArgumentNullException.ThrowIfNull(update);

            _update = update;

            InstalledVersion = AppVersionInfo.Version;
            AvailableVersion = update.Version.ToString(3);

            InitializeComponent();

            DataContext = this;
        }

        private void Later_Click(
            object sender,
            RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void ViewRelease_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _update.ReleaseNotesUrl.AbsoluteUri,
                    UseShellExecute = true
                });

                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "Impossible d'ouvrir la page de publication.\n\n"
                    + ex.Message,
                    "NFOForge",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }
}
