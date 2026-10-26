
using System;
using System.IO;
using System.Text.Json;

namespace NFOForge.App.Services
{
    public sealed class UpdatePreferences
    {
        public bool CheckOnStartup { get; set; } = true;
    }

    public sealed class UpdatePreferencesService
    {
        private readonly string _filePath;

        public UpdatePreferencesService(string? filePath = null)
        {
            _filePath = filePath ?? Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "NFOForge",
                "update-preferences.json");
        }

        public UpdatePreferences Load()
        {
            try
            {
                if (!File.Exists(_filePath))
                    return new UpdatePreferences();

                string json = File.ReadAllText(_filePath);

                return JsonSerializer.Deserialize<UpdatePreferences>(json)
                    ?? new UpdatePreferences();
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException
                or JsonException)
            {
                // Une préférence illisible ne bloque pas l'application.
                return new UpdatePreferences();
            }
        }

        public void Save(UpdatePreferences preferences)
        {
            ArgumentNullException.ThrowIfNull(preferences);

            string? directory = Path.GetDirectoryName(_filePath);

            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string temporaryFile = _filePath + "." +
                Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                string json = JsonSerializer.Serialize(
                    preferences,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                File.WriteAllText(temporaryFile, json);

                File.Move(
                    temporaryFile,
                    _filePath,
                    overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryFile))
                    File.Delete(temporaryFile);
            }
        }
    }
}
