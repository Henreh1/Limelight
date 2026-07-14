using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
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
        private const string ReleaseApiUrl =
            "https://api.github.com/repos/UE4SS-RE/RE-UE4SS/releases/tags/experimental-latest";

        private static readonly HttpClient HttpClient =
            CreateHttpClient();

        public async Task<Ue4ssPackageDownload> DownloadAsync(
            CancellationToken cancellationToken = default)
        {
            string? downloadedFile = null;

            try
            {
                using HttpResponseMessage releaseResponse =
                    await HttpClient.GetAsync(
                        ReleaseApiUrl,
                        cancellationToken);

                releaseResponse.EnsureSuccessStatusCode();

                await using Stream releaseStream =
                    await releaseResponse.Content.ReadAsStreamAsync(
                        cancellationToken);

                using JsonDocument document =
                    await JsonDocument.ParseAsync(
                        releaseStream,
                        cancellationToken:
                            cancellationToken);

                JsonElement release =
                    document.RootElement;

                JsonElement selectedAsset =
                    FindStandardPackage(release);

                string assetName =
                    selectedAsset
                        .GetProperty("name")
                        .GetString()
                    ?? throw new InvalidDataException(
                        "The UE4SS package has no filename.");

                string downloadUrl =
                    selectedAsset
                        .GetProperty("browser_download_url")
                        .GetString()
                    ?? throw new InvalidDataException(
                        "The UE4SS package has no download address.");

                string releaseName =
                    GetReleaseName(release);

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
                        Path.GetFileName(assetName));

                await DownloadFileAsync(
                    downloadUrl,
                    downloadedFile,
                    cancellationToken);

                bool digestVerified =
                    await VerifyPublishedDigestAsync(
                        selectedAsset,
                        downloadedFile,
                        cancellationToken);

                ValidatePackage(downloadedFile);

                return new Ue4ssPackageDownload
                {
                    PackagePath = downloadedFile,
                    ReleaseName = releaseName,
                    DigestVerified = digestVerified
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

        private static JsonElement FindStandardPackage(
            JsonElement release)
        {
            foreach (JsonElement asset in
                     release.GetProperty("assets")
                         .EnumerateArray())
            {
                string assetName =
                    asset.GetProperty("name").GetString()
                    ?? string.Empty;

                bool isStandardPackage =
                    assetName.StartsWith(
                        "UE4SS_",
                        StringComparison.OrdinalIgnoreCase) &&
                    assetName.EndsWith(
                        ".zip",
                        StringComparison.OrdinalIgnoreCase) &&
                    !assetName.Contains(
                        "zDEV",
                        StringComparison.OrdinalIgnoreCase);

                if (isStandardPackage)
                {
                    return asset;
                }
            }

            throw new InvalidDataException(
                "The official UE4SS release does not contain a standard Windows package.");
        }

        private static string GetReleaseName(
            JsonElement release)
        {
            if (release.TryGetProperty(
                    "name",
                    out JsonElement nameElement))
            {
                string? releaseName =
                    nameElement.GetString();

                if (!string.IsNullOrWhiteSpace(releaseName))
                {
                    return releaseName;
                }
            }

            return release.GetProperty("tag_name").GetString()
                ?? "UE4SS experimental";
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

        private static async Task<bool> VerifyPublishedDigestAsync(
            JsonElement asset,
            string packagePath,
            CancellationToken cancellationToken)
        {
            if (!asset.TryGetProperty(
                    "digest",
                    out JsonElement digestElement) ||
                digestElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            string? publishedDigest =
                digestElement.GetString();

            if (string.IsNullOrWhiteSpace(publishedDigest) ||
                !publishedDigest.StartsWith(
                    "sha256:",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string expectedHash =
                publishedDigest["sha256:".Length..]
                    .Trim();

            await using FileStream packageStream =
                File.OpenRead(packagePath);

            byte[] actualHash =
                await SHA256.HashDataAsync(
                    packageStream,
                    cancellationToken);

            string actualHashText =
                Convert.ToHexString(actualHash);

            if (!string.Equals(
                    expectedHash,
                    actualHashText,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The UE4SS package did not match its published SHA-256 digest.");
            }

            return true;
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