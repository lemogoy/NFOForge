
using System;
using System.IO;

namespace NFOForge.App.Services
{
    public sealed class NfoFileSaveService
    {
        public void Save(string destination, byte[] content)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(destination);
            ArgumentNullException.ThrowIfNull(content);

            string fullPath = Path.GetFullPath(destination);

            string directory = Path.GetDirectoryName(fullPath)
                ?? throw new IOException("Dossier de destination invalide.");

            // Le fichier temporaire reste sur le même volume.
            string temporaryPath = Path.Combine(
                directory,
                "." + Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                // CreateNew interdit d'écraser un fichier temporaire existant.
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                {
                    stream.Write(content);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(fullPath))
                {
                    // Remplacement atomique sur un système
                    // de fichiers qui prend en charge File.Replace.
                    File.Replace(
                        temporaryPath,
                        fullPath,
                        destinationBackupFileName: null);
                }
                else
                {
                    // Pas d'écrasement en cas de création concurrente.
                    File.Move(temporaryPath, fullPath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }
}
