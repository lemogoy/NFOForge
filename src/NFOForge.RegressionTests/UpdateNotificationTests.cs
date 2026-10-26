
using System;
using NFOForge.App.Services;

internal static class UpdateNotificationTests
{
    public static void Run(Action<bool, string> check)
    {
        ArgumentNullException.ThrowIfNull(check);

        var update = new UpdateInfo(
            new Version(1, 0, 2),
            new Uri("https://guidessence.fr/setup.exe"),
            new Uri("https://guidessence.fr/releases/1.0.2"),
            new string('A', 64));

        var available = new UpdateCheckResult(
            UpdateCheckStatus.UpdateAvailable,
            update);

        var upToDate = new UpdateCheckResult(
            UpdateCheckStatus.UpToDate);

        var unavailable = new UpdateCheckResult(
            UpdateCheckStatus.Unavailable);

        // 1. Nouvelle version détectée.
        check(
            UpdateNotificationPolicy.ShouldNotify(
                available, true, false),
            "Notification absente pour une nouvelle version.");

        // 2. Application déjà à jour.
        check(
            !UpdateNotificationPolicy.ShouldNotify(
                upToDate, true, false),
            "Notification affichée pour une version à jour.");

        // 3. Serveur indisponible.
        check(
            !UpdateNotificationPolicy.ShouldNotify(
                unavailable, true, false),
            "Notification affichée après une erreur réseau.");

        // 4. Vérification automatique désactivée.
        check(
            !UpdateNotificationPolicy.ShouldNotify(
                available, false, false),
            "Préférence utilisateur ignorée.");

        // 5. Application en cours de fermeture.
        check(
            !UpdateNotificationPolicy.ShouldNotify(
                available, true, true),
            "Notification autorisée pendant la fermeture.");

        // 6. Résultat incohérent sans informations.
        check(
            !UpdateNotificationPolicy.ShouldNotify(
                new UpdateCheckResult(
                    UpdateCheckStatus.UpdateAvailable),
                true,
                false),
            "Notification sans informations de version.");
    }
}
