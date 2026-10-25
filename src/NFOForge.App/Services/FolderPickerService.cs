using Microsoft.Win32;

namespace NFOForge.App.Services
{
    public sealed class FolderPickerService
    {
        public string? SelectFolder()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Sélectionner le dossier de la release",
                Multiselect = false
            };

            return dialog.ShowDialog() == true
                ? dialog.FolderName
                : null;
        }
    }
}