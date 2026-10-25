using System;

namespace NFOForge.App
{
    /// <summary>
    /// Fournit les informations de version de l'application.
    /// La version provient des métadonnées de l'assembly.
    /// </summary>
    public static class AppVersionInfo
    {
        public static string Version =>
            typeof(AppVersionInfo).Assembly
                .GetName()
                .Version?
                .ToString(3) ?? "0.0.0";

        public static string WindowTitle =>
            $"NFOForge {Version} — Guidessence";

        public static string FooterText =>
            $"NFOForge {Version}";
    }
}