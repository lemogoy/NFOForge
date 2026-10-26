
using System;

namespace NFOForge.App.Services
{
    /// <summary>
    /// Détermine si une notification de mise à jour
    /// doit être présentée à l'utilisateur.
    ///
    /// Cette classe n'effectue aucune opération réseau
    /// et n'ouvre aucune fenêtre.
    /// </summary>
    public static class UpdateNotificationPolicy
    {
        public static bool ShouldNotify(
            UpdateCheckResult result,
            bool checkOnStartup,
            bool isClosing)
        {
            ArgumentNullException.ThrowIfNull(result);

            return checkOnStartup
                && !isClosing
                && result.Status == UpdateCheckStatus.UpdateAvailable
                && result.Update is not null;
        }
    }
}
