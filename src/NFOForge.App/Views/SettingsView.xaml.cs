
using System.Windows.Controls;
using NFOForge.App.ViewModels;

namespace NFOForge.App.Views
{
    /// <summary>
    /// Vue des paramètres de NFOForge.
    /// Le ViewModel gère les mises à jour et les préférences.
    /// </summary>
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();

            DataContext = new UpdateSettingsViewModel();
        }
    }
}
