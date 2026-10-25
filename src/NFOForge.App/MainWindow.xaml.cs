using System.ComponentModel;
using System.Windows;
using NFOForge.App.ViewModels;

namespace NFOForge.App
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            if (DataContext is MainViewModel viewModel &&
                !viewModel.ConfirmCloseProject())
            {
                e.Cancel = true;
            }
        }
    }
}
