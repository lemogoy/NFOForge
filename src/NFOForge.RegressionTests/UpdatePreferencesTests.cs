
using System;
using System.IO;
using System.Text.Json;
using NFOForge.App.Services;

internal static class UpdatePreferencesTests
{
    public static void Run(Action<bool, string> check, string root)
    {
        ArgumentNullException.ThrowIfNull(check);

        string directory = Path.Combine(root, "update-preferences");
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, "preferences.json");
        var service = new UpdatePreferencesService(path);

        // 1. Sans fichier, contrôle automatique activé.
        check(service.Load().CheckOnStartup,
            "Préférence par défaut incorrecte.");

        // 2. Enregistrer la désactivation.
        service.Save(new UpdatePreferences
        {
            CheckOnStartup = false
        });

        check(File.Exists(path),
            "Fichier de préférences non créé.");

        // 3. Relire la préférence enregistrée.
        check(!service.Load().CheckOnStartup,
            "Désactivation non conservée.");

        // 4. Vérifier le JSON.
        using (var document = JsonDocument.Parse(
            File.ReadAllText(path)))
        {
            check(
                !document.RootElement
                    .GetProperty("CheckOnStartup")
                    .GetBoolean(),
                "Valeur JSON incorrecte.");
        }

        // 5. Réactiver la vérification.
        service.Save(new UpdatePreferences
        {
            CheckOnStartup = true
        });

        check(service.Load().CheckOnStartup,
            "Réactivation non conservée.");

        // 6. Vérifier la gestion d'un JSON invalide.
        File.WriteAllText(path, "{ JSON invalide");

        check(service.Load().CheckOnStartup,
            "JSON corrompu non géré.");

        // 7. Vérifier qu'aucun fichier temporaire
        // n'est conservé après une sauvegarde réussie.
        service.Save(new UpdatePreferences
        {
            CheckOnStartup = false
        });

        check(
            Directory.GetFiles(
                directory,
                "*.tmp").Length == 0,
            "Fichier temporaire résiduel.");
    }
}
