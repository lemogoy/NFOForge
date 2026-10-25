using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NFOForge.Compliance;
using System.Security.Cryptography;
using System.Threading;

namespace NFOForge.App.Services
{
    /// <summary>
    /// Inventaire statique des fichiers d'une release.
    /// Aucun fichier analysé n'est exécuté.
    /// </summary>
    /// 

    public sealed record FileFingerprint(
        string RelativePath,
        long SizeBytes,
        string Sha256);

    public sealed record InventoryProgress(
        int ProcessedFiles,
        int TotalFiles,
        string CurrentFile)
    {
        public int Percentage =>
            TotalFiles == 0
                ? 100
                : (int)Math.Round(
                    ProcessedFiles * 100.0 / TotalFiles);
    }

    public sealed class ReleaseInventoryService
    {


        private static string ComputeSha256(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1024 * 1024,
                FileOptions.SequentialScan);

            using var sha256 = SHA256.Create();

            byte[] buffer = new byte[1024 * 1024];

            int bytesRead;

            while ((bytesRead = stream.Read(
                buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                sha256.TransformBlock(
                    buffer, 0, bytesRead, buffer, 0);
            }

            cancellationToken.ThrowIfCancellationRequested();

            sha256.TransformFinalBlock(
                Array.Empty<byte>(), 0, 0);

            return Convert.ToHexString(sha256.Hash!);
        }


        private static int CountFiles(
            string rootPath,
            CancellationToken cancellationToken)
        {
            int count = 0;

            var pending = new Stack<string>();
            pending.Push(rootPath);

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string directory = pending.Pop();

                string[] entries;

                try
                {
                    entries = Directory.GetFileSystemEntries(directory);
                }
                catch (Exception ex) when (
                    ex is UnauthorizedAccessException or IOException)
                {
                    // Le scan principal consignera les avertissements.
                    continue;
                }

                foreach (string entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        FileAttributes attributes = File.GetAttributes(entry);

                        // Ne jamais suivre les liens ou jonctions.
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                            continue;

                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            pending.Push(entry);
                        }
                        else
                        {
                            checked { count++; }
                        }
                    }
                    catch (Exception ex) when (
                        ex is UnauthorizedAccessException or IOException)
                    {
                        // Le scan principal traitera l'erreur.
                    }
                }
            }

            return count;
        }

        public ReleaseInventoryResult Scan(
            string folderPath,
            IProgress<InventoryProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                throw new ArgumentException(
                    "Le chemin du dossier est obligatoire.",
                    nameof(folderPath));

            string rootPath = Path.GetFullPath(folderPath);

            if (!Directory.Exists(rootPath))
                throw new DirectoryNotFoundException(
                    $"Le dossier est introuvable : {rootPath}");

            var files = new List<ReleaseFile>();
            var warnings = new List<string>();
            var fingerprints = new List<FileFingerprint>();

            long totalSize = 0;
            int skippedDirectories = 0;
            int skippedFiles = 0;
            int processedFiles = 0;
            int totalFiles = CountFiles(rootPath, cancellationToken);
            progress?.Report(new InventoryProgress(
                0,
                totalFiles,
              "Préparation de l'inventaire..."));

            var pending = new Stack<string>();
            pending.Push(rootPath);

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string currentDirectory = pending.Pop();

                IEnumerable<string> entries;

                try
                {
                    entries = Directory
                        .EnumerateFileSystemEntries(currentDirectory)
                        .ToArray();
                }
                catch (Exception ex) when (
                    ex is UnauthorizedAccessException
                    or IOException)
                {
                    warnings.Add(
                        $"Dossier inaccessible : {currentDirectory}");

                    skippedDirectories++;
                    continue;
                }

                foreach (string entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        FileAttributes attributes =
                            File.GetAttributes(entry);

                        // Ne pas suivre les liens symboliques,
                        // jonctions et autres points de réanalyse.
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            warnings.Add(
                                $"Lien ignoré : {entry}");

                            skippedFiles++;
                            continue;
                        }

                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            pending.Push(entry);
                            continue;
                        }

                        var fileInfo = new FileInfo(entry);

                        string relativePath =
                            Path.GetRelativePath(rootPath, entry);

                        relativePath = relativePath.Replace('\\', '/');


                        long sizeBefore = fileInfo.Length;
                        DateTime modifiedBefore = fileInfo.LastWriteTimeUtc;

                        // Calcul de l'empreinte sans exécuter le fichier.
                        string sha256 = ComputeSha256(entry, cancellationToken);

                        fileInfo.Refresh();

                        long sizeAfter = fileInfo.Length;
                        DateTime modifiedAfter = fileInfo.LastWriteTimeUtc;

                        // Ne pas accepter silencieusement un fichier
                        // modifié pendant son analyse.
                        if (sizeBefore != sizeAfter ||
                            modifiedBefore != modifiedAfter)
                        {
                            throw new IOException(
                                "Le fichier a changé pendant le calcul SHA-256.");
                        }

                        files.Add(new ReleaseFile(
                            relativePath,
                            sizeAfter));

                        fingerprints.Add(new FileFingerprint(
                            relativePath,
                            sizeAfter,
                            sha256));


                        checked
                        {
                            totalSize += fileInfo.Length;
                        }
                    }
                    catch (Exception ex) when (
                        ex is UnauthorizedAccessException
                        or IOException
                        or OverflowException)
                    {
                        warnings.Add(
                            $"Fichier inaccessible ou taille invalide : {entry}");

                        skippedFiles++;

                    }

                    finally
                    {
                        // Les répertoires et les liens ignorés ne doivent
                        // pas être comptabilisés comme des fichiers traités.
                        // Ce compteur sera alimenté uniquement pour
                        // les fichiers réellement rencontrés.
                        if (File.Exists(entry))
                        {
                            processedFiles++;

                            string relativePath =
                                Path.GetRelativePath(rootPath, entry)
                                    .Replace('\\', '/');

                            progress?.Report(new InventoryProgress(
                                processedFiles,
                                totalFiles,
                                relativePath));
                        }
                    }

                }
            }

            return new ReleaseInventoryResult(
                rootPath,
                files.OrderBy(
                    f => f.RelativePath,
                    StringComparer.OrdinalIgnoreCase).ToArray(),
                totalSize,
                warnings,
                skippedDirectories,
                skippedFiles)
                        {
                            Fingerprints = fingerprints.OrderBy(
                    f => f.RelativePath,
                    StringComparer.OrdinalIgnoreCase).ToArray()
                        };
        }
    }


    public sealed record ReleaseInventoryResult(
        string FolderPath,
        IReadOnlyList<ReleaseFile> Files,
        long TotalSizeBytes,
        IReadOnlyList<string> Warnings,
        int SkippedDirectories,
        int SkippedFiles)
    {
        public IReadOnlyList<FileFingerprint> Fingerprints { get; init; }
            = Array.Empty<FileFingerprint>();

        public int FileCount => Files.Count;

        public bool IsComplete =>
            Warnings.Count == 0
            && SkippedDirectories == 0
            && SkippedFiles == 0
            && Fingerprints.Count == Files.Count;
    }

}