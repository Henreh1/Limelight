using Limelight.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Limelight.Services
{
    public sealed class NexusApiService
    {
        private const string ApiRoot =
            "https://api.nexusmods.com/v1/";

        private const string ValidateEndpoint =
            ApiRoot + "users/validate.json";

        private const string DeadAsDiscoModsEndpoint =
            ApiRoot + "games/deadasdisco/mods/";

        private const string DeadAsDiscoGameEndpoint =
            ApiRoot + "games/deadasdisco.json";

        private const string GraphQlEndpoint =
            "https://api.nexusmods.com/v2/graphql";

        private const int GraphQlBatchSize = 500;

        private const int GraphQlPageSize = 80;

        private const string ModsByUidQuery =
            "query ModsByUid($uids: [ID!]!, $count: Int, $offset: Int) {" +
            " modsByUid(uids: $uids, count: $count, offset: $offset) {" +
            " totalCount nodesCount" +
            " nodes {" +
            " modId name summary author version pictureUrl" +
            " endorsements downloads status category" +
            " }" +
            " }" +
            " }";

        private readonly HttpClient _httpClient;
        private readonly HttpClient _downloadClient;
        private readonly object _usageLock =
    new();

        private int _requestsThisSession;

        private NexusApiUsageSnapshot _usageSnapshot =
            new();

        public event Action<NexusApiUsageSnapshot>? UsageChanged;

        public NexusApiUsageSnapshot UsageSnapshot
        {
            get
            {
                lock (_usageLock)
                {
                    return _usageSnapshot;
                }
            }
        }

        private const int MaximumRecentModDetails = 60;

        private static readonly TimeSpan CatalogueCacheLifetime =
            TimeSpan.FromMinutes(15);

        private readonly SemaphoreSlim _catalogueLock =
            new(1, 1);

        private readonly Dictionary<string, List<long>> _feedOrders =
            new(StringComparer.OrdinalIgnoreCase);

        private IReadOnlyList<NexusModSummary> _catalogueCache =
            Array.Empty<NexusModSummary>();

        private DateTimeOffset _catalogueCachedAt;

        private int? _deadAsDiscoGameId;

        private readonly Dictionary<int, string> _categoryNames =
            new();

        public NexusApiService()
        {
            _httpClient =
                new HttpClient
                {
                    Timeout =
                        TimeSpan.FromSeconds(20)
                };

            _downloadClient =
                new HttpClient
                {
                    Timeout =
                        Timeout.InfiniteTimeSpan
                };
        }

        public async Task<NexusAccount> ValidateApiKeyAsync(
            string apiKey,
            CancellationToken cancellationToken = default)
        {
            ValidateApiKey(apiKey);

            using HttpRequestMessage request =
                CreateRequest(
                    ValidateEndpoint,
                    apiKey);

            using HttpResponseMessage response =
                await SendRequestAsync(
                    request,
                    cancellationToken);

            EnsureSuccessfulResponse(response);

            string json =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            NexusValidationResponse? result =
                JsonSerializer.Deserialize<NexusValidationResponse>(
                    json);

            if (result is null ||
                string.IsNullOrWhiteSpace(result.Name))
            {
                throw new InvalidOperationException(
                    "Nexus Mods returned an incomplete account response.");
            }

            return new NexusAccount
            {
                UserId = result.UserId,
                Name = result.Name,
                IsPremium = result.IsPremium,
                IsSupporter = result.IsSupporter
            };
        }

        public async Task<IReadOnlyList<NexusModSummary>> GetModsAsync(
            string apiKey,
            string sortKey,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            ValidateApiKey(apiKey);

            await _catalogueLock.WaitAsync(
                cancellationToken);

            try
            {
                bool cacheIsFresh =
                    _catalogueCache.Count > 0 &&
                    DateTimeOffset.UtcNow -
                    _catalogueCachedAt <
                    CatalogueCacheLifetime;

                if (forceRefresh ||
                    !cacheIsFresh)
                {
                    await RefreshCatalogueAsync(
                        apiKey,
                        cancellationToken);
                }

                return OrderCatalogue(
                    sortKey);
            }
            finally
            {
                _catalogueLock.Release();
            }
        }

        public async Task<NexusModSummary> GetModAsync(
            string apiKey,
            long modId,
            CancellationToken cancellationToken = default)
        {
            ValidateApiKey(apiKey);

            if (modId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(modId),
                    "A Nexus mod ID must be greater than zero.");
            }

            await GetDeadAsDiscoGameIdAsync(
                apiKey,
                cancellationToken);

            NexusModSummary? mod =
                await GetModOrNullAsync(
                    apiKey,
                    modId,
                    cancellationToken);

            if (mod is null)
            {
                throw new InvalidOperationException(
                    $"Dead as Disco mod {modId} could not be found on Nexus Mods.");
            }

            return mod;
        }

        public async Task<IReadOnlyList<NexusModFile>> GetModFilesAsync(
            string apiKey,
            long modId,
            CancellationToken cancellationToken = default)
        {
            ValidateApiKey(apiKey);

            if (modId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(modId),
                    "A Nexus mod ID must be greater than zero.");
            }

            string endpoint =
                DeadAsDiscoModsEndpoint +
                $"{modId}/files.json";

            using HttpRequestMessage request =
                CreateRequest(
                    endpoint,
                    apiKey);

            using HttpResponseMessage response =
                await SendRequestAsync(
                    request,
                    cancellationToken);

            EnsureSuccessfulResponse(response);

            string json =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            NexusModFilesResponse? result =
                JsonSerializer.Deserialize<NexusModFilesResponse>(
                    json);

            if (result?.Files is null)
            {
                return Array.Empty<NexusModFile>();
            }

            return result.Files
                .Where(file =>
                    file.FileId > 0)
                .Select(file =>
                    new NexusModFile
                    {
                        ModId = modId,
                        FileId = file.FileId,
                        CategoryId = file.CategoryId,
                        CategoryName = FirstAvailable(
                            file.CategoryName,
                            "FILE").ToUpperInvariant(),
                        FileName = FirstAvailable(
                            file.Name,
                            file.FileName,
                            "Unnamed file"),
                        ArchiveName =
                            file.FileName?.Trim() ??
                            string.Empty,
                        Description = FirstAvailable(
                            file.Description,
                            "No description was provided for this file."),
                        Version =
                            file.Version?.Trim() ??
                            string.Empty,
                        SizeKilobytes =
                            Math.Max(
                                file.SizeKilobytes,
                                file.Size),
                        UploadedTimestamp =
                            file.UploadedTimestamp,
                        IsPrimary =
                            file.IsPrimary
                    })
                .ToList();
        }

        public async Task<string> DownloadModFileAsync(
            string apiKey,
            NexusModFile file,
            IProgress<NexusDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(file);
            ValidateApiKey(apiKey);

            if (file.ModId <= 0 ||
                file.FileId <= 0)
            {
                throw new ArgumentException(
                    "A valid Nexus mod file is required.",
                    nameof(file));
            }

            Uri downloadUri =
                await GetDownloadUriAsync(
                    apiKey,
                    file.ModId,
                    file.FileId,
                    cancellationToken);

            string downloadDirectory =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "Limelight",
                    "Downloads");

            Directory.CreateDirectory(
                downloadDirectory);

            string archiveName =
                CreateSafeArchiveName(file);

            string finalPath =
                Path.Combine(
                    downloadDirectory,
                    archiveName);

            string temporaryPath =
                finalPath + ".download";

            TryDeleteFile(temporaryPath);

            try
            {
                using HttpRequestMessage request =
                    new(
                        HttpMethod.Get,
                        downloadUri);

                request.Headers.TryAddWithoutValidation(
                    "Application-Name",
                    "Limelight");

                request.Headers.TryAddWithoutValidation(
                    "Application-Version",
                    "0.1.0");

                using HttpResponseMessage response =
                    await _downloadClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);

                response.EnsureSuccessStatusCode();

                long? totalBytes =
                    response.Content.Headers.ContentLength;

                await using Stream source =
                    await response.Content.ReadAsStreamAsync(
                        cancellationToken);

                await using FileStream destination =
                    new(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        81920,
                        useAsync: true);

                byte[] buffer =
                    new byte[81920];

                long bytesReceived = 0;
                int bytesRead;

                while ((bytesRead =
                    await source.ReadAsync(
                        buffer,
                        cancellationToken)) > 0)
                {
                    await destination.WriteAsync(
                        buffer.AsMemory(0, bytesRead),
                        cancellationToken);

                    bytesReceived += bytesRead;

                    progress?.Report(
                        new NexusDownloadProgress
                        {
                            BytesReceived = bytesReceived,
                            TotalBytes = totalBytes
                        });
                }

                await destination.FlushAsync(
                    cancellationToken);

                if (bytesReceived == 0)
                {
                    throw new InvalidDataException(
                        "Nexus Mods returned an empty download.");
                }

                File.Move(
                    temporaryPath,
                    finalPath,
                    overwrite: true);

                return finalPath;
            }
            catch
            {
                TryDeleteFile(temporaryPath);
                throw;
            }
        }

        public static void DeleteDownloadedArchive(
            string archivePath)
        {
            TryDeleteFile(archivePath);
        }

        private async Task<Uri> GetDownloadUriAsync(
            string apiKey,
            long modId,
            int fileId,
            CancellationToken cancellationToken)
        {
            string endpoint =
                DeadAsDiscoModsEndpoint +
                $"{modId}/files/{fileId}/download_link.json";

            using HttpRequestMessage request =
                CreateRequest(
                    endpoint,
                    apiKey);

            using HttpResponseMessage response =
                await SendRequestAsync(
                    request,
                    cancellationToken);

            if (response.StatusCode ==
                HttpStatusCode.Forbidden)
            {
                throw new InvalidOperationException(
                    "Nexus direct downloads require a Premium account during personal-key testing. " +
                    "Open this file on Nexus Mods if the connected account uses manual downloads.");
            }

            EnsureSuccessfulResponse(response);

            string json =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            List<NexusDownloadLinkResponse>? links =
                JsonSerializer.Deserialize<List<NexusDownloadLinkResponse>>(
                    json);

            string? uriText =
                links?
                    .Select(link => link.Uri)
                    .FirstOrDefault(value =>
                        !string.IsNullOrWhiteSpace(value));

            if (!Uri.TryCreate(
                    uriText,
                    UriKind.Absolute,
                    out Uri? downloadUri) ||
                downloadUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException(
                    "Nexus Mods did not return a secure download link.");
            }

            return downloadUri;
        }

        private static string CreateSafeArchiveName(
            NexusModFile file)
        {
            string requestedName =
                FirstAvailable(
                    file.ArchiveName,
                    file.FileName,
                    $"mod-{file.ModId}-file-{file.FileId}.zip");

            string safeName =
                string.Join(
                    "_",
                    requestedName.Split(
                        Path.GetInvalidFileNameChars(),
                        StringSplitOptions.RemoveEmptyEntries));

            if (!Path.GetExtension(safeName).Equals(
                    ".zip",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "This Nexus file is not a ZIP archive. Limelight currently installs ZIP downloads only.");
            }

            return $"{file.ModId}-{file.FileId}-{safeName}";
        }

        private static void TryDeleteFile(
            string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // A partial download can be removed by the next attempt.
            }
        }

        private async Task RefreshCatalogueAsync(
            string apiKey,
            CancellationToken cancellationToken)
        {
            await GetDeadAsDiscoGameIdAsync(
                apiKey,
                cancellationToken);

            string[] sortKeys =
            {
                "latest_added",
                "latest_updated",
                "trending"
            };

            Task<IReadOnlyList<NexusModSummary>>[] feedTasks =
                sortKeys
                    .Select(sortKey =>
                        GetFeedAsync(
                            apiKey,
                            sortKey,
                            cancellationToken))
                    .ToArray();

            IReadOnlyList<NexusModSummary>[] feeds =
                await Task.WhenAll(feedTasks);

            _feedOrders.Clear();

            var catalogue =
                new List<NexusModSummary>();

            var knownIds =
                new HashSet<long>();

            for (int index = 0;
                index < sortKeys.Length;
                index++)
            {
                IReadOnlyList<NexusModSummary> feed =
                    feeds[index];

                _feedOrders[sortKeys[index]] =
                    feed
                        .Select(mod => mod.ModId)
                        .ToList();

                foreach (NexusModSummary mod in feed)
                {
                    if (knownIds.Add(mod.ModId))
                    {
                        catalogue.Add(mod);
                    }
                }
            }

            try
            {
                long highestKnownModId =
                    feeds
                        .SelectMany(feed => feed)
                        .Select(mod => mod.ModId)
                        .DefaultIfEmpty(0)
                        .Max();

                IReadOnlyList<NexusModSummary> completeCatalogue =
                    await GetCompleteModCatalogueAsync(
                        apiKey,
                        highestKnownModId,
                        cancellationToken);

                foreach (NexusModSummary mod in
                    completeCatalogue)
                {
                    if (knownIds.Add(mod.ModId))
                    {
                        catalogue.Add(mod);
                    }
                }
            }
            catch (Exception exception) when (
                exception is HttpRequestException or
                InvalidOperationException or
                JsonException)
            {
                // Older Nexus accounts may not have access to every GraphQL
                // feature, so the legacy monthly catalogue remains a fallback.
                await AppendRecentlyUpdatedModsAsync(
                    apiKey,
                    catalogue,
                    knownIds,
                    cancellationToken);
            }

            _catalogueCache =
                catalogue;

            _catalogueCachedAt =
                DateTimeOffset.UtcNow;
        }

        private async Task<IReadOnlyList<NexusModSummary>> GetCompleteModCatalogueAsync(
            string apiKey,
            long highestKnownModId,
            CancellationToken cancellationToken)
        {
            if (highestKnownModId <= 0 ||
                highestKnownModId > int.MaxValue)
            {
                return Array.Empty<NexusModSummary>();
            }

            int gameId =
                await GetDeadAsDiscoGameIdAsync(
                    apiKey,
                    cancellationToken);

            var catalogue =
                new List<NexusModSummary>();

            IEnumerable<int> possibleModIds =
                Enumerable.Range(
                    1,
                    checked((int)highestKnownModId));

            foreach (int[] modIdBatch in
                possibleModIds.Chunk(GraphQlBatchSize))
            {
                string[] uids =
                    modIdBatch
                        .Select(modId =>
                            CreateModUid(
                                gameId,
                                modId))
                        .ToArray();

                IReadOnlyList<NexusModSummary> batch =
                    await GetModsByUidAsync(
                        apiKey,
                        uids,
                        cancellationToken);

                catalogue.AddRange(batch);
            }

            return catalogue
                .GroupBy(mod => mod.ModId)
                .Select(group => group.First())
                .ToList();
        }

        private async Task<int> GetDeadAsDiscoGameIdAsync(
            string apiKey,
            CancellationToken cancellationToken)
        {
            if (_deadAsDiscoGameId is > 0)
            {
                return _deadAsDiscoGameId.Value;
            }

            using HttpRequestMessage request =
                CreateRequest(
                    DeadAsDiscoGameEndpoint,
                    apiKey);

            using HttpResponseMessage response =
                await SendRequestAsync(
                    request,
                    cancellationToken);

            EnsureSuccessfulResponse(response);

            string json =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            NexusGameResponse? result =
                JsonSerializer.Deserialize<NexusGameResponse>(
                    json);

            if (result is null ||
                result.Id <= 0)
            {
                throw new InvalidOperationException(
                    "Nexus Mods did not return the Dead as Disco game ID.");
            }

            _deadAsDiscoGameId =
                result.Id;

            _categoryNames.Clear();

            foreach (NexusCategoryResponse category in
                result.Categories ??
                new List<NexusCategoryResponse>())
            {
                if (category.CategoryId > 0 &&
                    !string.IsNullOrWhiteSpace(category.Name))
                {
                    _categoryNames[category.CategoryId] =
                        category.Name.Trim();
                }
            }

            return result.Id;
        }

        private async Task<IReadOnlyList<NexusModSummary>> GetModsByUidAsync(
            string apiKey,
            IReadOnlyList<string> uids,
            CancellationToken cancellationToken)
        {
            var catalogue =
                new List<NexusModSummary>();

            int offset = 0;

            while (true)
            {
                using HttpRequestMessage request =
                    CreateGraphRequest(
                        apiKey,
                        uids,
                        GraphQlPageSize,
                        offset);

                using HttpResponseMessage response =
                    await SendRequestAsync(
                        request,
                        cancellationToken);

                EnsureSuccessfulResponse(response);

                string json =
                    await response.Content.ReadAsStringAsync(
                        cancellationToken);

                NexusGraphEnvelope? result =
                    JsonSerializer.Deserialize<NexusGraphEnvelope>(
                        json);

                NexusGraphModConnection? connection =
                    result?
                        .Data?
                        .ModsByUid;

                if (connection?.Nodes is null)
                {
                    string graphError =
                        result?.Errors?
                            .Select(error => error.Message)
                            .FirstOrDefault(message =>
                                !string.IsNullOrWhiteSpace(message)) ??
                        "Nexus Mods did not return the requested title catalogue.";

                    throw new InvalidOperationException(
                        graphError);
                }

                catalogue.AddRange(
                    connection.Nodes
                        .Where(node => node is not null)
                        .Select(node => MapGraphMod(node!))
                        .Where(mod => mod is not null)
                        .Cast<NexusModSummary>());

                offset +=
                    GraphQlPageSize;

                int totalCount =
                    Math.Max(
                        connection.TotalCount,
                        connection.NodesCount);

                // Nexus currently caps this connection at 80 nodes per page.
                // Walking the offset is what turns each UID batch into a full
                // catalogue instead of quietly accepting its first 20 items.
                if (connection.Nodes.Count == 0 ||
                    offset >= totalCount)
                {
                    break;
                }
            }

            return catalogue
                .GroupBy(mod => mod.ModId)
                .Select(group => group.First())
                .ToList();
        }

        private async Task AppendRecentlyUpdatedModsAsync(
            string apiKey,
            List<NexusModSummary> catalogue,
            HashSet<long> knownIds,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<long> recentlyUpdatedIds =
                await GetRecentlyUpdatedModIdsAsync(
                    apiKey,
                    cancellationToken);

            Dictionary<long, NexusModSummary> previousModsById =
                _catalogueCache.ToDictionary(
                    mod => mod.ModId);

            var uncachedRecentIds =
                new List<long>();

            foreach (long modId in recentlyUpdatedIds)
            {
                if (modId <= 0 ||
                    knownIds.Contains(modId))
                {
                    continue;
                }

                if (previousModsById.TryGetValue(
                        modId,
                        out NexusModSummary? cachedMod))
                {
                    knownIds.Add(modId);
                    catalogue.Add(cachedMod);
                    continue;
                }

                uncachedRecentIds.Add(modId);
            }

            long[] missingIds =
                uncachedRecentIds
                    .Take(MaximumRecentModDetails)
                    .ToArray();

            // Small batches keep Limelight responsive without sending Nexus
            // a large burst of requests all at once.
            foreach (long[] batch in
                missingIds.Chunk(4))
            {
                NexusModSummary?[] details =
                    await Task.WhenAll(
                        batch.Select(modId =>
                            GetModOrNullAsync(
                                apiKey,
                                modId,
                                cancellationToken)));

                foreach (NexusModSummary? mod in details)
                {
                    if (mod is not null &&
                        knownIds.Add(mod.ModId))
                    {
                        catalogue.Add(mod);
                    }
                }
            }
        }

        private static string CreateModUid(
            int gameId,
            int modId)
        {
            ulong uid =
                ((ulong)(uint)gameId << 32) |
                (uint)modId;

            return uid.ToString(
                CultureInfo.InvariantCulture);
        }

        private async Task<IReadOnlyList<NexusModSummary>> GetFeedAsync(
            string apiKey,
            string sortKey,
            CancellationToken cancellationToken)
        {
            string endpoint =
                DeadAsDiscoModsEndpoint +
                GetSortEndpoint(sortKey);

            using HttpRequestMessage request =
                CreateRequest(
                    endpoint,
                    apiKey);

            using HttpResponseMessage response =
                await SendRequestAsync(
                    request,
                    cancellationToken);

            EnsureSuccessfulResponse(response);

            string json =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            List<NexusModResponse>? results =
                JsonSerializer.Deserialize<List<NexusModResponse>>(
                    json);

            if (results is null)
            {
                throw new InvalidOperationException(
                    "Nexus Mods returned an incomplete mod list.");
            }

            return results
                .Select(MapMod)
                .Where(mod => mod is not null)
                .Cast<NexusModSummary>()
                .ToList();
        }

        private async Task<IReadOnlyList<long>> GetRecentlyUpdatedModIdsAsync(
            string apiKey,
            CancellationToken cancellationToken)
        {
            string endpoint =
                DeadAsDiscoModsEndpoint +
                "updated.json?period=1m";

            using HttpRequestMessage request =
                CreateRequest(
                    endpoint,
                    apiKey);

            using HttpResponseMessage response =
                await SendRequestAsync(
                    request,
                    cancellationToken);

            EnsureSuccessfulResponse(response);

            string json =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            List<NexusRecentUpdateResponse>? results =
                JsonSerializer.Deserialize<List<NexusRecentUpdateResponse>>(
                    json);

            if (results is null)
            {
                return Array.Empty<long>();
            }

            return results
                .Where(result =>
                    result.ModId > 0)
                .OrderByDescending(result =>
                    Math.Max(
                        result.LatestFileUpdate,
                        result.LatestModActivity))
                .Select(result => result.ModId)
                .Distinct()
                .ToList();
        }

        private async Task<NexusModSummary?> GetModOrNullAsync(
            string apiKey,
            long modId,
            CancellationToken cancellationToken)
        {
            string endpoint =
                DeadAsDiscoModsEndpoint +
                $"{modId}.json";

            using HttpRequestMessage request =
                CreateRequest(
                    endpoint,
                    apiKey);

            using HttpResponseMessage response =
                await SendRequestAsync(
                    request,
                    cancellationToken);

            if (response.StatusCode ==
                HttpStatusCode.NotFound)
            {
                return null;
            }

            EnsureSuccessfulResponse(response);

            string json =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            NexusModResponse? result =
                JsonSerializer.Deserialize<NexusModResponse>(
                    json);

            return result is null
                ? null
                : MapMod(result);
        }

        private IReadOnlyList<NexusModSummary> OrderCatalogue(
            string sortKey)
        {
            string normalisedSortKey =
                NormaliseSortKey(sortKey);

            if (!_feedOrders.TryGetValue(
                    normalisedSortKey,
                    out List<long>? priorityIds))
            {
                return _catalogueCache;
            }

            Dictionary<long, NexusModSummary> modsById =
                _catalogueCache.ToDictionary(
                    mod => mod.ModId);

            var ordered =
                new List<NexusModSummary>();

            var addedIds =
                new HashSet<long>();

            foreach (long modId in priorityIds)
            {
                if (modsById.TryGetValue(
                        modId,
                        out NexusModSummary? mod) &&
                    addedIds.Add(modId))
                {
                    ordered.Add(mod);
                }
            }

            foreach (NexusModSummary mod in
                _catalogueCache)
            {
                if (addedIds.Add(mod.ModId))
                {
                    ordered.Add(mod);
                }
            }

            return ordered;
        }

        private NexusModSummary? MapMod(
            NexusModResponse result)
        {
            // Hidden or moderated entries can contain very little useful
            // information, so they are left out of the Limelight browser.
            if (!result.Available ||
                result.ModId <= 0 ||
                string.IsNullOrWhiteSpace(result.Name))
            {
                return null;
            }

            return new NexusModSummary
            {
                ModId = result.ModId,
                Name = result.Name.Trim(),
                Summary = result.Summary?.Trim() ??
                    "No description has been provided.",
                Description = result.Description?.Trim() ??
                    string.Empty,
                Author = FirstAvailable(
                    result.Author,
                    result.UploadedBy,
                    "UNKNOWN AUTHOR"),
                Version = FirstAvailable(
                    result.Version,
                    "UNKNOWN"),
                CategoryName =
                    _categoryNames.TryGetValue(
                        result.CategoryId,
                        out string? categoryName)
                        ? categoryName
                        : "MISCELLANEOUS",
                PictureUrl = result.PictureUrl?.Trim() ??
                    string.Empty,
                Endorsements = result.EndorsementCount,
                TotalDownloads = result.ModDownloads
            };
        }

        private static NexusModSummary? MapGraphMod(
            NexusGraphModResponse result)
        {
            bool isPublished =
                string.IsNullOrWhiteSpace(result.Status) ||
                result.Status.Equals(
                    "published",
                    StringComparison.OrdinalIgnoreCase) ||
                result.Status.Equals(
                    "publish_with_game",
                    StringComparison.OrdinalIgnoreCase);

            if (!isPublished ||
                result.ModId <= 0 ||
                string.IsNullOrWhiteSpace(result.Name))
            {
                return null;
            }

            return new NexusModSummary
            {
                ModId = result.ModId,
                Name = result.Name.Trim(),
                Summary = result.Summary?.Trim() ??
                    "No description has been provided.",
                Author = FirstAvailable(
                    result.Author,
                    "UNKNOWN AUTHOR"),
                Version = FirstAvailable(
                    result.Version,
                    "UNKNOWN"),
                CategoryName = FirstAvailable(
                    result.Category,
                    "DEAD AS DISCO"),
                PictureUrl = result.PictureUrl?.Trim() ??
                    string.Empty,
                Endorsements = result.Endorsements,
                TotalDownloads = result.Downloads
            };
        }

        private static string NormaliseSortKey(
            string sortKey)
        {
            return sortKey switch
            {
                "latest_updated" =>
                    "latest_updated",

                "trending" =>
                    "trending",

                _ =>
                    "latest_added"
            };
        }

        private static string GetSortEndpoint(
            string sortKey)
        {
            return sortKey switch
            {
                "latest_updated" =>
                    "latest_updated.json",

                "trending" =>
                    "trending.json",

                _ =>
                    "latest_added.json"
            };
        }

        private async Task<HttpResponseMessage> SendRequestAsync(
    HttpRequestMessage request,
    CancellationToken cancellationToken)
        {
            if (UsageSnapshot.ShouldPauseRequests)
            {
                throw new InvalidOperationException(
                    "Limelight paused Nexus API testing because the connected account's remaining request quota is low.");
            }

            HttpResponseMessage? response =
                null;

            try
            {
                response =
                    await _httpClient.SendAsync(
                        request,
                        cancellationToken);

                return response;
            }
            finally
            {
                RecordNexusRequest(
                    request,
                    response);
            }
        }

        private void RecordNexusRequest(
            HttpRequestMessage request,
            HttpResponseMessage? response)
        {
            int? dailyRemaining =
                ReadQuotaHeader(
                    response,
                    "x-rl-daily-remaining");

            int? hourlyRemaining =
                ReadQuotaHeader(
                    response,
                    "x-rl-hourly-remaining");

            NexusApiUsageSnapshot snapshot;

            lock (_usageLock)
            {
                _requestsThisSession++;

                snapshot =
                    new NexusApiUsageSnapshot
                    {
                        RequestsThisSession =
                            _requestsThisSession,

                        DailyRemaining =
                            dailyRemaining ??
                            _usageSnapshot.DailyRemaining,

                        HourlyRemaining =
                            hourlyRemaining ??
                            _usageSnapshot.HourlyRemaining,

                        LastRequestUtc =
                            DateTimeOffset.UtcNow,

                        LastRequestKind =
                            DescribeRequest(request)
                    };

                _usageSnapshot =
                    snapshot;
            }

            try
            {
                UsageChanged?.Invoke(
                    snapshot);
            }
            catch
            {
                // I never let the testing display interfere with a completed Nexus request.
            }
        }

        private static int? ReadQuotaHeader(
            HttpResponseMessage? response,
            string headerName)
        {
            if (response is null ||
                !response.Headers.TryGetValues(
                    headerName,
                    out IEnumerable<string>? values))
            {
                return null;
            }

            string? value =
                values.FirstOrDefault();

            return int.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int parsedValue)
                ? parsedValue
                : null;
        }

        private static string DescribeRequest(
            HttpRequestMessage request)
        {
            string path =
                request.RequestUri?
                    .AbsolutePath
                    .ToLowerInvariant() ??
                string.Empty;

            if (path.Contains(
                    "/users/validate",
                    StringComparison.Ordinal))
            {
                return "ACCOUNT VALIDATION";
            }

            if (path.Contains(
                    "/download_link",
                    StringComparison.Ordinal))
            {
                return "DOWNLOAD LINK";
            }

            if (path.EndsWith(
                    "/files.json",
                    StringComparison.Ordinal))
            {
                return "FILE LIST";
            }

            if (path.Contains(
                    "/graphql",
                    StringComparison.Ordinal))
            {
                return "CATALOGUE";
            }

            if (path.Contains(
                    "/mods/",
                    StringComparison.Ordinal))
            {
                return "MOD DETAILS";
            }

            return "CATALOGUE";
        }
        private static HttpRequestMessage CreateRequest(
            string endpoint,
            string apiKey)
        {
            var request =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    endpoint);

            AddRequestHeaders(
                request,
                apiKey);

            return request;
        }

        private static HttpRequestMessage CreateGraphRequest(
            string apiKey,
            IReadOnlyList<string> uids,
            int count,
            int offset)
        {
            string payload =
                JsonSerializer.Serialize(
                    new
                    {
                        query = ModsByUidQuery,
                        variables = new
                        {
                            uids,
                            count,
                            offset
                        }
                    });

            var request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    GraphQlEndpoint)
                {
                    Content =
                        new StringContent(
                            payload,
                            Encoding.UTF8,
                            "application/json")
                };

            AddRequestHeaders(
                request,
                apiKey);

            return request;
        }

        private static void AddRequestHeaders(
            HttpRequestMessage request,
            string apiKey)
        {
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue(
                    "application/json"));

            request.Headers.TryAddWithoutValidation(
                "apikey",
                apiKey.Trim());

            // Nexus asks third-party applications to identify themselves so
            // unusual traffic can be traced to the correct Limelight version.
            request.Headers.TryAddWithoutValidation(
                "Application-Name",
                "Limelight");

            request.Headers.TryAddWithoutValidation(
                "Application-Version",
                "0.1.0");

            request.Headers.TryAddWithoutValidation(
                "Protocol-Version",
                "1.7.1");
        }

        private static void ValidateApiKey(
            string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException(
                    "A Nexus Mods API key is required.",
                    nameof(apiKey));
            }
        }

        private static void EnsureSuccessfulResponse(
            HttpResponseMessage response)
        {
            if (response.StatusCode is
                HttpStatusCode.Unauthorized or
                HttpStatusCode.Forbidden)
            {
                throw new UnauthorizedAccessException(
                    "Nexus Mods did not accept this API key.");
            }

            if (response.StatusCode ==
                HttpStatusCode.TooManyRequests)
            {
                throw new InvalidOperationException(
                    "The Nexus Mods request limit has been reached. Please try again later.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Nexus Mods returned status {(int)response.StatusCode}.");
            }
        }

        private static string FirstAvailable(
            params string?[] values)
        {
            foreach (string? value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        private sealed class NexusValidationResponse
        {
            [JsonPropertyName("user_id")]
            public long UserId { get; set; }

            [JsonPropertyName("name")]
            public string Name { get; set; } =
                string.Empty;

            [JsonPropertyName("is_premium")]
            public bool IsPremium { get; set; }

            [JsonPropertyName("is_supporter")]
            public bool IsSupporter { get; set; }
        }

        private sealed class NexusModResponse
        {
            [JsonPropertyName("mod_id")]
            public long ModId { get; set; }

            [JsonPropertyName("category_id")]
            public int CategoryId { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("summary")]
            public string? Summary { get; set; }

            [JsonPropertyName("description")]
            public string? Description { get; set; }

            [JsonPropertyName("author")]
            public string? Author { get; set; }

            [JsonPropertyName("uploaded_by")]
            public string? UploadedBy { get; set; }

            [JsonPropertyName("version")]
            public string? Version { get; set; }

            [JsonPropertyName("picture_url")]
            public string? PictureUrl { get; set; }

            [JsonPropertyName("endorsement_count")]
            public int EndorsementCount { get; set; }

            [JsonPropertyName("mod_downloads")]
            public int ModDownloads { get; set; }

            [JsonPropertyName("available")]
            public bool Available { get; set; } =
                true;
        }

        private sealed class NexusModFilesResponse
        {
            [JsonPropertyName("files")]
            public List<NexusModFileResponse>? Files { get; set; }
        }

        private sealed class NexusDownloadLinkResponse
        {
            [JsonPropertyName("URI")]
            public string? Uri { get; set; }
        }

        private sealed class NexusModFileResponse
        {
            [JsonPropertyName("file_id")]
            public int FileId { get; set; }

            [JsonPropertyName("category_id")]
            public int CategoryId { get; set; }

            [JsonPropertyName("category_name")]
            public string? CategoryName { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("file_name")]
            public string? FileName { get; set; }

            [JsonPropertyName("description")]
            public string? Description { get; set; }

            [JsonPropertyName("version")]
            public string? Version { get; set; }

            [JsonPropertyName("size")]
            public long Size { get; set; }

            [JsonPropertyName("size_kb")]
            public long SizeKilobytes { get; set; }

            [JsonPropertyName("uploaded_timestamp")]
            public long UploadedTimestamp { get; set; }

            [JsonPropertyName("is_primary")]
            public bool IsPrimary { get; set; }
        }

        private sealed class NexusRecentUpdateResponse
        {
            [JsonPropertyName("mod_id")]
            public long ModId { get; set; }

            [JsonPropertyName("latest_file_update")]
            public long LatestFileUpdate { get; set; }

            [JsonPropertyName("latest_mod_activity")]
            public long LatestModActivity { get; set; }
        }

        private sealed class NexusGameResponse
        {
            [JsonPropertyName("id")]
            public int Id { get; set; }

            [JsonPropertyName("categories")]
            public List<NexusCategoryResponse>? Categories { get; set; }
        }

        private sealed class NexusCategoryResponse
        {
            [JsonPropertyName("category_id")]
            public int CategoryId { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }
        }

        private sealed class NexusGraphEnvelope
        {
            [JsonPropertyName("data")]
            public NexusGraphData? Data { get; set; }

            [JsonPropertyName("errors")]
            public List<NexusGraphError>? Errors { get; set; }
        }

        private sealed class NexusGraphError
        {
            [JsonPropertyName("message")]
            public string Message { get; set; } =
                string.Empty;
        }

        private sealed class NexusGraphData
        {
            [JsonPropertyName("modsByUid")]
            public NexusGraphModConnection? ModsByUid { get; set; }
        }

        private sealed class NexusGraphModConnection
        {
            [JsonPropertyName("totalCount")]
            public int TotalCount { get; set; }

            [JsonPropertyName("nodesCount")]
            public int NodesCount { get; set; }

            [JsonPropertyName("nodes")]
            public List<NexusGraphModResponse?>? Nodes { get; set; }
        }

        private sealed class NexusGraphModResponse
        {
            [JsonPropertyName("modId")]
            public long ModId { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("summary")]
            public string? Summary { get; set; }

            [JsonPropertyName("description")]
            public string? Description { get; set; }

            [JsonPropertyName("author")]
            public string? Author { get; set; }

            [JsonPropertyName("version")]
            public string? Version { get; set; }

            [JsonPropertyName("pictureUrl")]
            public string? PictureUrl { get; set; }

            [JsonPropertyName("endorsements")]
            public int Endorsements { get; set; }

            [JsonPropertyName("downloads")]
            public int Downloads { get; set; }

            [JsonPropertyName("status")]
            public string? Status { get; set; }

            [JsonPropertyName("category")]
            public string? Category { get; set; }
        }
    }
}
