using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Limelight.Services
{
    public sealed class Ue4ssPackageDownload
    {
        public string PackagePath { get; init; } =
            string.Empty;

        public string ReleaseName { get; init; } =
            string.Empty;

        public bool DigestVerified { get; init; }
    }

    public sealed class Ue4ssReleaseService
    {
        public const string CompatibleVersion =
            "3.0.1-1009";

        public const string CompatibleCommit =
            "c2ac2464";

        public const string CompatiblePackageName =
            "UE4SS_v3.0.1-1009-gc2ac2464.zip";

        public const string CompatibleReleaseName =
            "UE4SS v3.0.1-1009 (c2ac2464)";

        public const string CompatibleDllSha256 =
            "A79B894D4A499C066985B47354D2A3A1FC9069CEBEFE585BA458BB8F572930B5";

        private const string CompatiblePackageUrl =
            "https://github.com/UE4SS-RE/RE-UE4SS/releases/download/experimental/" +
            CompatiblePackageName;

        private const string CompatiblePackageSha256 =
            "BA53BFE27B82895A6A4D0B98C3ACD93E93E913C27134F82C09F619C7C1AAA4C6";

        private static readonly HttpClient HttpClient =
            CreateHttpClient();

        public async Task<Ue4ssPackageDownload> DownloadAsync(
            CancellationToken cancellationToken = default)
        {
            string? downloadedFile = null;

            try
            {
                string downloadDirectory =
                    Path.Combine(
                        Path.GetTempPath(),
                        "Limelight",
                        "LiveLoader");

                Directory.CreateDirectory(
                    downloadDirectory);

                downloadedFile =
                    Path.Combine(
                        downloadDirectory,
                        CompatiblePackageName);

                await DownloadFileAsync(
                    CompatiblePackageUrl,
                    downloadedFile,
                    cancellationToken);

                await VerifyPackageDigestAsync(
                    downloadedFile,
                    cancellationToken);

                ValidatePackage(downloadedFile);

                return new Ue4ssPackageDownload
                {
                    PackagePath = downloadedFile,
                    ReleaseName = CompatibleReleaseName,
                    DigestVerified = true
                };
            }
            catch
            {
                // An interrupted or invalid download should never be reused by
                // the next setup attempt.
                if (!string.IsNullOrWhiteSpace(downloadedFile) &&
                    File.Exists(downloadedFile))
                {
                    File.Delete(downloadedFile);
                }

                throw;
            }
        }

        private static HttpClient CreateHttpClient()
        {
            HttpClient client =
                new HttpClient();

            // GitHub requires API clients to identify themselves.
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Limelight-Mod-Manager/1.0");

            client.Timeout =
                TimeSpan.FromMinutes(5);

            return client;
        }

        private static async Task DownloadFileAsync(
            string downloadUrl,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            using HttpResponseMessage response =
                await HttpClient.GetAsync(
                    downloadUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

            response.EnsureSuccessStatusCode();

            await using Stream source =
                await response.Content.ReadAsStreamAsync(
                    cancellationToken);

            await using FileStream destination =
                new FileStream(
                    destinationPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    useAsync: true);

            await source.CopyToAsync(
                destination,
                cancellationToken);
        }

        private static async Task VerifyPackageDigestAsync(
            string packagePath,
            CancellationToken cancellationToken)
        {
            await using FileStream packageStream =
                File.OpenRead(packagePath);

            byte[] actualHash =
                await SHA256.HashDataAsync(
                    packageStream,
                    cancellationToken);

            string actualHashText =
                Convert.ToHexString(actualHash);

            if (!string.Equals(
                    CompatiblePackageSha256,
                    actualHashText,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The UE4SS package did not match its published SHA-256 digest.");
            }
        }

        private static void ValidatePackage(
            string packagePath)
        {
            using ZipArchive archive =
                ZipFile.OpenRead(packagePath);

            HashSet<string> files =
                archive.Entries
                    .Where(entry =>
                        !string.IsNullOrWhiteSpace(entry.Name))
                    .Select(entry =>
                        NormalizeEntryPath(entry.FullName))
                    .ToHashSet(
                        StringComparer.OrdinalIgnoreCase);

            bool hasProxy =
                files.Contains("dwmapi.dll");

            bool hasModernLayout =
                files.Contains("ue4ss/UE4SS.dll") &&
                files.Contains(
                    "ue4ss/UE4SS-settings.ini");

            bool hasLegacyLayout =
                files.Contains("UE4SS.dll") &&
                files.Contains("UE4SS-settings.ini");

            if (!hasProxy ||
                (!hasModernLayout && !hasLegacyLayout))
            {
                throw new InvalidDataException(
                    "The downloaded ZIP does not contain a complete UE4SS installation.");
            }
        }

        private static string NormalizeEntryPath(
            string entryPath)
        {
            string normalized =
                entryPath
                    .Replace('\\', '/')
                    .TrimStart('/');

            string[] pathParts =
                normalized.Split(
                    '/',
                    StringSplitOptions.RemoveEmptyEntries);

            if (pathParts.Any(part => part == ".."))
            {
                throw new InvalidDataException(
                    "The UE4SS ZIP contains an unsafe file path.");
            }

            return string.Join(
                "/",
                pathParts);
        }
    }
}
