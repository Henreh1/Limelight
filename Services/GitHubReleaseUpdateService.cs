using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Limelight.Services
{
    public sealed record GitHubReleaseUpdate(
        string Version,
        string Name,
        string Url);

    public sealed class GitHubReleaseUpdateService
    {
        private const string ReleasesEndpoint =
            "https://api.github.com/repos/Henreh1/Limelight/releases?per_page=10";

        private static readonly HttpClient Client =
            CreateClient();

        public async Task<GitHubReleaseUpdate?> CheckForUpdateAsync(
            string currentVersion,
            CancellationToken cancellationToken = default)
        {
            if (!TryParseVersion(
                    currentVersion,
                    out ParsedVersion? installedVersion) ||
                installedVersion == null)
            {
                return null;
            }

            try
            {
                using CancellationTokenSource timeout =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);

                timeout.CancelAfter(
                    TimeSpan.FromSeconds(6));

                using HttpRequestMessage request =
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        ReleasesEndpoint);

                using HttpResponseMessage response =
                    await Client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        timeout.Token);

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                await using Stream content =
                    await response.Content.ReadAsStreamAsync(
                        timeout.Token);

                using JsonDocument document =
                    await JsonDocument.ParseAsync(
                        content,
                        cancellationToken: timeout.Token);

                GitHubReleaseUpdate? newestRelease =
                    null;

                ParsedVersion? newestVersion =
                    null;

                foreach (JsonElement release in
                    document.RootElement.EnumerateArray())
                {
                    if (release.TryGetProperty(
                            "draft",
                            out JsonElement draft) &&
                        draft.GetBoolean())
                    {
                        continue;
                    }

                    if (!TryReadString(
                            release,
                            "tag_name",
                            out string tagName) ||
                        !TryParseVersion(
                            tagName,
                            out ParsedVersion? releaseVersion) ||
                        releaseVersion == null)
                    {
                        continue;
                    }

                    if (!TryReadString(
                            release,
                            "html_url",
                            out string releaseUrl) ||
                        !Uri.TryCreate(
                            releaseUrl,
                            UriKind.Absolute,
                            out Uri? releaseUri) ||
                        !string.Equals(
                            releaseUri.Host,
                            "github.com",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (newestVersion != null &&
                        CompareVersions(
                            releaseVersion,
                            newestVersion) <= 0)
                    {
                        continue;
                    }

                    string releaseName =
                        TryReadString(
                            release,
                            "name",
                            out string name) &&
                        !string.IsNullOrWhiteSpace(name)
                            ? name
                            : tagName;

                    newestVersion =
                        releaseVersion;

                    newestRelease =
                        new GitHubReleaseUpdate(
                            tagName,
                            releaseName,
                            releaseUri.AbsoluteUri);
                }

                if (newestRelease == null ||
                    newestVersion == null ||
                    CompareVersions(
                        newestVersion,
                        installedVersion) <= 0)
                {
                    return null;
                }

                return newestRelease;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (HttpRequestException)
            {
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static HttpClient CreateClient()
        {
            HttpClient client =
                new HttpClient();

            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Limelight-Update-Checker");

            client.DefaultRequestHeaders.Accept.ParseAdd(
                "application/vnd.github+json");

            client.DefaultRequestHeaders.Add(
                "X-GitHub-Api-Version",
                "2022-11-28");

            return client;
        }

        private static bool TryReadString(
            JsonElement element,
            string propertyName,
            out string value)
        {
            value =
                string.Empty;

            if (!element.TryGetProperty(
                    propertyName,
                    out JsonElement property) ||
                property.ValueKind !=
                    JsonValueKind.String)
            {
                return false;
            }

            value =
                property.GetString() ??
                string.Empty;

            return !string.IsNullOrWhiteSpace(value);
        }

        private static bool TryParseVersion(
            string value,
            out ParsedVersion? version)
        {
            version =
                null;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            int versionStart =
                -1;

            for (int index = 0;
                index < value.Length;
                index++)
            {
                if (char.IsDigit(value[index]))
                {
                    versionStart =
                        index;
                    break;
                }
            }

            if (versionStart < 0)
            {
                return false;
            }

            string cleanVersion =
                value[versionStart..];

            int metadataStart =
                cleanVersion.IndexOf('+');

            if (metadataStart >= 0)
            {
                cleanVersion =
                    cleanVersion[..metadataStart];
            }

            string coreText =
                cleanVersion;

            string prereleaseText =
                string.Empty;

            int prereleaseStart =
                cleanVersion.IndexOf('-');

            if (prereleaseStart >= 0)
            {
                coreText =
                    cleanVersion[..prereleaseStart];

                prereleaseText =
                    cleanVersion[(prereleaseStart + 1)..];
            }

            string[] coreParts =
                coreText.Split(
                    '.',
                    StringSplitOptions.RemoveEmptyEntries);

            if (coreParts.Length == 0 ||
                coreParts.Length > 4)
            {
                return false;
            }

            int[] core =
                new int[4];

            for (int index = 0;
                index < coreParts.Length;
                index++)
            {
                if (!int.TryParse(
                        coreParts[index],
                        out core[index]))
                {
                    return false;
                }
            }

            string[] prerelease =
                string.IsNullOrWhiteSpace(prereleaseText)
                    ? Array.Empty<string>()
                    : prereleaseText.Split(
                        new[] { '.', '-' },
                        StringSplitOptions.RemoveEmptyEntries);

            version =
                new ParsedVersion(
                    core,
                    prerelease);

            return true;
        }

        private static int CompareVersions(
            ParsedVersion left,
            ParsedVersion right)
        {
            for (int index = 0;
                index < left.Core.Length;
                index++)
            {
                int coreComparison =
                    left.Core[index].CompareTo(
                        right.Core[index]);

                if (coreComparison != 0)
                {
                    return coreComparison;
                }
            }

            bool leftIsStable =
                left.Prerelease.Length == 0;

            bool rightIsStable =
                right.Prerelease.Length == 0;

            if (leftIsStable || rightIsStable)
            {
                return leftIsStable.CompareTo(
                    rightIsStable);
            }

            int sharedLength =
                Math.Min(
                    left.Prerelease.Length,
                    right.Prerelease.Length);

            for (int index = 0;
                index < sharedLength;
                index++)
            {
                string leftPart =
                    left.Prerelease[index];

                string rightPart =
                    right.Prerelease[index];

                bool leftIsNumber =
                    int.TryParse(
                        leftPart,
                        out int leftNumber);

                bool rightIsNumber =
                    int.TryParse(
                        rightPart,
                        out int rightNumber);

                int partComparison;

                if (leftIsNumber &&
                    rightIsNumber)
                {
                    partComparison =
                        leftNumber.CompareTo(
                            rightNumber);
                }
                else if (leftIsNumber !=
                    rightIsNumber)
                {
                    partComparison =
                        leftIsNumber
                            ? -1
                            : 1;
                }
                else
                {
                    partComparison =
                        string.Compare(
                            leftPart,
                            rightPart,
                            StringComparison.OrdinalIgnoreCase);
                }

                if (partComparison != 0)
                {
                    return partComparison;
                }
            }

            return left.Prerelease.Length.CompareTo(
                right.Prerelease.Length);
        }

        private sealed record ParsedVersion(
            int[] Core,
            string[] Prerelease);
    }
}
