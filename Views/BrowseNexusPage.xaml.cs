using Limelight.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace Limelight.Views
{
        public partial class BrowseNexusPage : UserControl
        {
            private const int ModsPerPage = 12;
            private const string NexusHomeUrl = "https://www.nexusmods.com/deadasdisco/mods/";

        public event Action<string>? SearchRequested;
        public event Action<string>? SortChanged;
        public event Action<string>? CategoryChanged;
        public event Action? RefreshRequested;
        public event Action<long, int>? ModManagerDownloadRequested;
        public event Action? NexusOAuthLoginRequested;
        public event Action? NexusUseApiKeyRequested;

        private bool _isUpdatingCategories;
        private bool _isEmbeddedBrowserInitialized;

        private IReadOnlyList<NexusModSummary> _allMods =
            Array.Empty<NexusModSummary>();

        private int _currentPage = 1;

        public BrowseNexusPage()
        {
            InitializeComponent();

            InitialiseNexusBrowserAsync();
        }

        private async void InitialiseNexusBrowserAsync()
        {
            try
            {
                CoreWebView2Environment webEnvironment =
                    await CreateNexusBrowserEnvironmentAsync();

                await NexusBrowser.EnsureCoreWebView2Async(
                    webEnvironment);
                _isEmbeddedBrowserInitialized = true;

                if (NexusBrowser.CoreWebView2 is not null)
                {
                    NexusBrowser.CoreWebView2.NewWindowRequested +=
                        NexusBrowser_NewWindowRequested;
                }

                NexusBrowser.Source =
                    new Uri(NexusHomeUrl);

                UpdateNexusBrowserAddress(NexusHomeUrl);
                UpdateNexusBrowserNavigationState();
            }
            catch
            {
                // I keep the Nexus page in manual-browse fallback mode
                // when WebView2 is unavailable so browsing never blocks.
                ShowFallbackCatalogueMode();
            }
        }

        private static async Task<CoreWebView2Environment> CreateNexusBrowserEnvironmentAsync()
        {
            string preferredDataFolder =
                GetNexusBrowserDataFolder();

            try
            {
                return await CoreWebView2Environment.CreateAsync(
                    null,
                    preferredDataFolder,
                    null);
            }
            catch
            {
                return await CoreWebView2Environment.CreateAsync(
                    null,
                    GetFallbackNexusBrowserDataFolder(),
                    null);
            }
        }

        private static string GetNexusBrowserDataFolder()
        {
            string edgeUserDataFolder =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft",
                    "Edge",
                    "User Data");

            return Directory.Exists(edgeUserDataFolder)
                ? edgeUserDataFolder
                : GetFallbackNexusBrowserDataFolder();
        }

        private static string GetFallbackNexusBrowserDataFolder()
        {
            string fallbackFolder =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "Limelight",
                    "NexusBrowser");

            Directory.CreateDirectory(fallbackFolder);

            return fallbackFolder;
        }

        private void ShowFallbackCatalogueMode()
        {
            NexusBrowserFrame.Visibility =
                Visibility.Collapsed;

            NexusResultsScrollViewer.Visibility =
                Visibility.Visible;
        }

        public void ShowModDetails(
            NexusModSummary mod)
        {
            NavigateNexusBrowserWithMod(mod);
        }

        public void ShowModDetailsError(
            string message)
        {
            // I keep the old details compatibility path for this old overlay state.
            // I show the error in the embedded browser error area.
            NexusEmptyTitleText.Text =
                "THE MOD DETAIL PAGE DID NOT LOAD";

            NexusEmptyMessageText.Text = message;

            NexusEmptyState.Visibility =
                Visibility.Visible;
        }

        public void ShowModFiles(
            NexusModSummary mod,
            IEnumerable<NexusModFile> files)
        {
            // I replace modal file views with a direct browser launch to the
            // selected mod page.
            ShowModDetails(mod);
        }

        public void ShowModFilesError(
            string message)
        {
            ShowModDetailsError(message);
        }

        public void OpenNexusBrowserForConnectedSession()
        {
            _ = NavigateNexusBrowserAsync(
                NexusHomeUrl);
        }

        public void ShowDownloadState(
            NexusModFile file,
            string message,
            bool isBusy,
            int? percentage = null)
        {
            // I moved download progress to the dedicated Downloads page.
        }

        private void NavigateNexusBrowserWithMod(
            NexusModSummary mod)
        {
            _ = NavigateNexusBrowserAsync(
                $"{NexusHomeUrl}{mod.ModId}");
        }

        public string SelectedSortKey =>
            NexusSortBox.SelectedIndex switch
            {
                1 => "latest_updated",
                2 => "trending",
                _ => "latest_added"
            };

        public string SelectedCategory
        {
            get
            {
                string selected =
                    NexusCategoryBox.SelectedItem switch
                    {
                        ComboBoxItem item =>
                            item.Content?.ToString() ?? string.Empty,

                        string value =>
                            value,

                        _ =>
                            string.Empty
                    };

                return selected.Equals(
                        "ALL CATEGORIES",
                        StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : selected;
            }
        }

        public void ShowConnection(
            bool isConnected,
            string? accountName = null)
        {
            string displayName =
                string.IsNullOrWhiteSpace(accountName)
                    ? "NEXUS ACCOUNT"
                    : accountName;

            NexusAccountButtonStatusText.Text =
                isConnected
                    ? "CONNECTED"
                    : "ACCOUNT REQUIRED";

            NexusAccountButtonStatusText.Foreground =
                StatusBrush(isConnected);

            NexusAccountButtonHeaderText.Foreground =
                isConnected
                    ? (Brush)FindResource("CyanBrush")
                    : (Brush)FindResource("CyanBrush");

            NexusAccountLogoText.Foreground =
                NexusAccountButtonHeaderText.Foreground;

            NexusConnectionChevron.Stroke =
                isConnected
                    ? (Brush)FindResource("CyanBrush")
                    : (Brush)FindResource("CyanBrush");

            NexusAccountButton.Background =
                isConnected
                    ? (Brush)FindResource("NexusAccountButtonConnectedBrush")
                    : (Brush)FindResource("NexusDisconnectedButtonBrush");

            NexusAccountLogoBorder.Background =
                isConnected
                    ? (Brush)FindResource("NexusBrandButtonBrush")
                    : (Brush)FindResource("NexusDisconnectedButtonBrush");

            NexusAccountButton.BorderBrush =
                isConnected
                    ? (Brush)FindResource("NexusAccountButtonConnectedBorderBrush")
                    : (Brush)FindResource("NexusDisconnectedButtonBrush");

            NexusAccountButtonHeaderText.Text =
                "NEXUS MODS";

            NexusDropdownTitleText.Text =
                isConnected
                    ? $"CONNECTED TO {displayName.ToUpperInvariant()}"
                    : "Connect to Nexus Mods";

            NexusDropdownSubtitleText.Text =
                isConnected
                    ? "Your Nexus account is connected. Continue browsing and direct downloads are available."
                    : "Sign in with your Nexus Mods account using OAuth for a convenient login experience";

            NexusOAuthLoginButton.Visibility =
                Visibility.Visible;

            NexusUseApiKeyLink.Visibility =
                isConnected
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            if (isConnected)
            {
                NexusOAuthLoginButton.Content =
                    "OPEN SETTINGS";
            }
            else
            {
                NexusOAuthLoginButton.Content = "SIGN IN WITH NEXUS MODS";
            }

            NexusSearchBox.IsEnabled =
                isConnected;

            NexusSortBox.IsEnabled =
                isConnected;

            NexusCategoryBox.IsEnabled =
                isConnected;

            NexusSearchButton.IsEnabled =
                isConnected;

            NexusRefreshButton.IsEnabled =
                isConnected;

            if (!isConnected)
            {
                _allMods =
                    Array.Empty<NexusModSummary>();

                NexusModsList.ItemsSource =
                    null;

                NexusModsList.Visibility =
                    Visibility.Collapsed;

                NexusPaginationPanel.Visibility =
                    Visibility.Collapsed;

                NexusEmptyState.Visibility =
                    Visibility.Visible;

                NexusEmptyTitleText.Text =
                    "CONNECT YOUR NEXUS ACCOUNT";

                NexusEmptyMessageText.Text =
                    "Open Settings and connect Nexus Mods before browsing the Dead as Disco library.";

                NexusResultCountText.Text =
                    "CONNECT NEXUS TO BROWSE";

                NexusResultCountText.Foreground =
                    StatusBrush(isHealthy: false);

                return;
            }

            if (NexusModsList.Items.Count == 0)
            {
                NexusEmptyState.Visibility =
                    Visibility.Visible;

                NexusEmptyTitleText.Text =
                    "READY FOR THE NEXT ACT";

                NexusEmptyMessageText.Text =
                    "Refresh the page to load the latest Dead as Disco mods.";

                NexusResultCountText.Text =
                    "READY";

                NexusResultCountText.Foreground =
                    StatusBrush(isHealthy: true);
            }
        }

        public void ShowLoading(
            bool isLoading)
        {
            NexusLoadingPanel.Visibility =
                isLoading
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        public void ShowCategories(
            IEnumerable<string> categories)
        {
            string previousSelection =
                SelectedCategory;

            List<string> categoryNames =
                categories
                    .Where(category =>
                        !string.IsNullOrWhiteSpace(category) &&
                        !category.Equals(
                            "DEAD AS DISCO",
                            StringComparison.OrdinalIgnoreCase))
                    .Select(category => category.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(category => category)
                    .ToList();

            _isUpdatingCategories =
                true;

            try
            {
                NexusCategoryBox.Items.Clear();
                NexusCategoryBox.Items.Add(
                    "ALL CATEGORIES");

                foreach (string category in categoryNames)
                {
                    NexusCategoryBox.Items.Add(
                        category.ToUpperInvariant());
                }

                string previousUpper =
                    previousSelection.ToUpperInvariant();

                int previousIndex =
                    NexusCategoryBox.Items.IndexOf(
                        previousUpper);

                NexusCategoryBox.SelectedIndex =
                    previousIndex >= 0
                        ? previousIndex
                        : 0;
            }
            finally
            {
                _isUpdatingCategories =
                    false;
            }
        }

        public void ShowMods(
            IReadOnlyCollection<NexusModSummary> mods)
        {
            ShowLoading(isLoading: false);

            // I keep every matching result available, then render only the
            // current card page so a large catalogue stays fast.
            _allMods =
                mods.ToList();

            _currentPage =
                1;

            RenderCurrentPage();
        }

        private void RenderCurrentPage()
        {
            int resultCount =
                _allMods.Count;

            int pageCount =
                Math.Max(
                    1,
                    (int)Math.Ceiling(
                        resultCount / (double)ModsPerPage));

            _currentPage =
                Math.Clamp(
                    _currentPage,
                    1,
                    pageCount);

            int firstResultIndex =
                (_currentPage - 1) * ModsPerPage;

            IReadOnlyList<NexusModSummary> pageMods =
                _allMods
                    .Skip(firstResultIndex)
                    .Take(ModsPerPage)
                    .ToList();

            NexusModsList.ItemsSource =
                pageMods;

            bool hasMods =
                resultCount > 0;

            NexusModsList.Visibility =
                hasMods
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            NexusEmptyState.Visibility =
                hasMods
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            NexusResultCountText.Text =
                hasMods
                    ? resultCount == 1
                        ? "1 RESULT"
                        : $"{resultCount:N0} RESULTS"
                    : "NO RESULTS";

            NexusResultCountText.Foreground =
                StatusBrush(hasMods);

            NexusPaginationPanel.Visibility =
                pageCount > 1
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            NexusPreviousPageButton.IsEnabled =
                _currentPage > 1;

            NexusNextPageButton.IsEnabled =
                _currentPage < pageCount;

            NexusPageNumberText.Text =
                $"PAGE {_currentPage:N0} OF {pageCount:N0}";

            if (hasMods)
            {
                int firstResultNumber =
                    firstResultIndex + 1;

                int lastResultNumber =
                    Math.Min(
                        firstResultIndex + ModsPerPage,
                        resultCount);

                NexusPageRangeText.Text =
                    $"SHOWING {firstResultNumber:N0} TO {lastResultNumber:N0} OF {resultCount:N0}";
            }

            if (!hasMods)
            {
                NexusEmptyTitleText.Text =
                    "NO MODS FOUND";

                NexusEmptyMessageText.Text =
                    "Try another name, or paste a Dead as Disco Nexus mod link or mod ID for an exact lookup.";
            }
        }

        public void ShowError(
            string message)
        {
            ShowLoading(isLoading: false);

            NexusModsList.Visibility =
                Visibility.Collapsed;

            NexusPaginationPanel.Visibility =
                Visibility.Collapsed;

            NexusEmptyState.Visibility =
                Visibility.Visible;

            NexusEmptyTitleText.Text =
                "THE SETLIST COULD NOT LOAD";

            NexusEmptyMessageText.Text =
                message;

            NexusResultCountText.Text =
                "NEXUS ERROR";

            NexusResultCountText.Foreground =
                StatusBrush(isHealthy: false);
        }

        private void SearchButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            SearchRequested?.Invoke(
                NexusSearchBox.Text.Trim());
        }

        private void NexusSearchBox_KeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            SearchRequested?.Invoke(
                NexusSearchBox.Text.Trim());
        }

        private void NexusSortBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (!IsLoaded)
            {
                return;
            }

            SortChanged?.Invoke(
                SelectedSortKey);
        }

        private void NexusCategoryBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (!IsLoaded ||
                _isUpdatingCategories)
            {
                return;
            }

            CategoryChanged?.Invoke(
                SelectedCategory);
        }

        private void RefreshButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            RefreshRequested?.Invoke();
        }

        private void PreviousPageButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_currentPage <= 1)
            {
                return;
            }

            _currentPage--;
            RenderCurrentPage();
            ReturnToResultsTop();
        }

        private void NextPageButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            int pageCount =
                (int)Math.Ceiling(
                    _allMods.Count / (double)ModsPerPage);

            if (_currentPage >= pageCount)
            {
                return;
            }

            _currentPage++;
            RenderCurrentPage();
            ReturnToResultsTop();
        }

        private void ReturnToResultsTop()
        {
            // I return to the results heading so the next page does not open
            // halfway down the window after a long card grid.
            NexusResultsScrollViewer.ScrollToVerticalOffset(
                0);
        }

        private void NexusAccountButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            NexusAccountPopup.IsOpen =
                !NexusAccountPopup.IsOpen;
        }

        private void NexusOAuthLoginButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            NexusAccountPopup.IsOpen =
                false;

            NexusOAuthLoginRequested?.Invoke();
        }

        private void NexusUseApiKeyLink_Click(
            object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            NexusAccountPopup.IsOpen =
                false;

            e.Handled = true;
            NexusUseApiKeyRequested?.Invoke();
        }

        private void ViewFilesButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.Tag is NexusModSummary mod)
            {
                NavigateNexusBrowserWithMod(
                    mod);

                e.Handled = true;
            }
        }

        private void NexusBrowserBackButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!_isEmbeddedBrowserInitialized ||
                NexusBrowser.CoreWebView2 is null)
            {
                return;
            }

            if (NexusBrowser.CoreWebView2.CanGoBack)
            {
                NexusBrowser.CoreWebView2.GoBack();
            }
        }

        private void NexusBrowserForwardButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!_isEmbeddedBrowserInitialized ||
                NexusBrowser.CoreWebView2 is null)
            {
                return;
            }

            if (NexusBrowser.CoreWebView2.CanGoForward)
            {
                NexusBrowser.CoreWebView2.GoForward();
            }
        }

        private void NexusBrowserRefreshButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!_isEmbeddedBrowserInitialized ||
                NexusBrowser.CoreWebView2 is null)
            {
                return;
            }

            NexusBrowser.Reload();
        }

        private void NexusBrowserHomeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!_isEmbeddedBrowserInitialized ||
                NexusBrowser.CoreWebView2 is null)
            {
                return;
            }

            _ = NavigateNexusBrowserAsync(
                NexusHomeUrl);
        }

        private void NexusBrowser_NavigationStarting(
            object sender,
            CoreWebView2NavigationStartingEventArgs e)
        {
            if (!TryHandleNexusDownloadUri(
                e.Uri))
            {
                return;
            }

            e.Cancel = true;
        }

        private bool TryHandleNexusDownloadUri(
            string? rawUri)
        {
            if (!TryParseNexusDownloadUri(
                rawUri,
                out long modId,
                out int fileId))
            {
                return false;
            }

            ModManagerDownloadRequested?.Invoke(
                modId,
                fileId);

            return true;
        }

        private void NexusBrowser_NewWindowRequested(
            object? sender,
            CoreWebView2NewWindowRequestedEventArgs e)
        {
            if (TryHandleNexusDownloadUri(
                e.Uri))
            {
                e.Handled = true;
                return;
            }

            if (!Uri.TryCreate(
                    e.Uri,
                    UriKind.Absolute,
                    out Uri? uri))
            {
                e.Handled = true;
                return;
            }

            e.Handled = true;
            _ = NavigateNexusBrowserAsync(
                uri.AbsoluteUri);
        }

        private void NexusBrowserAddressBar_KeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;

            string address =
                NexusBrowserAddressBar.Text.Trim();

            if (string.IsNullOrWhiteSpace(address))
            {
                return;
            }

            if (TryHandleNexusDownloadUri(
                address))
            {
                return;
            }

            string targetAddress =
                address.StartsWith(
                    "http://",
                    StringComparison.OrdinalIgnoreCase) ||
                address.StartsWith(
                    "https://",
                    StringComparison.OrdinalIgnoreCase)
                    ? address
                    : $"https://{address}";

            _ = NavigateNexusBrowserAsync(
                targetAddress);
        }

        private void NexusBrowser_NavigationCompleted(
            object sender,
            CoreWebView2NavigationCompletedEventArgs e)
        {
            if (NexusBrowser.CoreWebView2 is null)
            {
                return;
            }

            string address =
                NexusBrowser.CoreWebView2.Source ??
                string.Empty;

            UpdateNexusBrowserAddress(address);
            UpdateNexusBrowserNavigationState();
        }

        private void UpdateNexusBrowserAddress(
            string address)
        {
            NexusBrowserAddressBar.Text =
                address;
        }

        private void UpdateNexusBrowserNavigationState()
        {
            if (NexusBrowser.CoreWebView2 is null)
            {
                NexusBrowserBackButton.IsEnabled =
                    false;
                NexusBrowserForwardButton.IsEnabled =
                    false;

                return;
            }

            NexusBrowserBackButton.IsEnabled =
                NexusBrowser.CoreWebView2.CanGoBack;

            NexusBrowserForwardButton.IsEnabled =
                NexusBrowser.CoreWebView2.CanGoForward;
        }

        private async Task NavigateNexusBrowserAsync(
            string address)
        {
            if (!_isEmbeddedBrowserInitialized ||
                NexusBrowser.CoreWebView2 is null ||
                !Uri.TryCreate(
                    address,
                    UriKind.Absolute,
                    out Uri? target))
            {
                return;
            }

            await NexusBrowser.EnsureCoreWebView2Async();
            NexusBrowser.Source = target;
        }

        private static bool TryParseNexusDownloadUri(
            string? rawUri,
            out long modId,
            out int fileId)
        {
            modId = 0;
            fileId = 0;

            if (string.IsNullOrWhiteSpace(rawUri) ||
                !Uri.TryCreate(rawUri, UriKind.Absolute, out Uri? uri) ||
                !uri.IsAbsoluteUri)
            {
                return false;
            }

            bool isNexusDownloadLink =
                uri.Scheme.Equals(
                    "nxm",
                    StringComparison.OrdinalIgnoreCase) ||
                IsNexusDownloadHost(uri.Host);

            if (!isNexusDownloadLink)
            {
                return false;
            }

            string path = uri.AbsolutePath;
            string[] segments =
                path
                    .Split(
                        '/',
                        StringSplitOptions.RemoveEmptyEntries)
                    .Select(part =>
                        Uri.UnescapeDataString(part))
                    .ToArray();

            long parsedModId = 0;
            int parsedFileId = 0;

            // I use the primary /mods/{modId}/files/{fileId} pattern first.
            for (int index = 0; index < segments.Length; index++)
            {
                if (segments[index].Equals(
                        "mods",
                        StringComparison.OrdinalIgnoreCase) &&
                    index + 1 < segments.Length &&
                    long.TryParse(
                        segments[index + 1],
                        out long candidateModId) &&
                    candidateModId > 0)
                {
                    parsedModId = candidateModId;
                }

                if (index + 1 < segments.Length &&
                    segments[index].Equals(
                        "files",
                        StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(
                        segments[index + 1],
                        out int candidateFileId) &&
                    candidateFileId > 0)
                {
                    parsedFileId = candidateFileId;
                }
            }

            if (parsedFileId == 0)
            {
                parsedFileId = GetNexusQueryValue(
                        uri.Query,
                        "file_id") ??
                    GetNexusQueryValue(
                        uri.Query,
                        "fileId") ??
                    GetNexusQueryValue(
                        uri.Query,
                        "file") ??
                    GetNexusQueryValue(
                        uri.Query,
                        "fid") ??
                    GetNexusQueryValue(
                        uri.Query,
                        "download_id") ??
                    0;
            }

            if (parsedModId == 0)
            {
                parsedModId = GetNexusLongQueryValue(
                        uri.Query,
                        "mod_id") ??
                    GetNexusLongQueryValue(
                        uri.Query,
                        "modId") ??
                    GetNexusLongQueryValue(
                        uri.Query,
                        "mod") ??
                    GetNexusLongQueryValue(
                        uri.Query,
                        "modid") ??
                    0;
            }

            if (parsedModId > 0 && parsedFileId > 0)
            {
                modId = parsedModId;
                fileId = parsedFileId;
                return true;
            }

            return false;
        }

        private static int? GetNxmQueryValue(
            string query,
            string key)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return null;
            }

            string[] pairs =
                query.TrimStart('?')
                    .Split(
                        '&',
                        StringSplitOptions.RemoveEmptyEntries);

            foreach (string pair in pairs)
            {
                string[] kvp =
                    pair.Split(
                        '=',
                        2);

                if (kvp.Length != 2)
                {
                    continue;
                }

                string keyText =
                    Uri.UnescapeDataString(
                        kvp[0])
                        .Trim();

                if (!keyText.Equals(
                    key,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (int.TryParse(
                    Uri.UnescapeDataString(kvp[1]),
                    out int value))
                {
                    return value;
                }

                return null;
            }

            return null;
        }

        private static long? GetNxmLongQueryValue(
            string query,
            string key)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return null;
            }

            string[] pairs =
                query.TrimStart('?')
                    .Split(
                        '&',
                        StringSplitOptions.RemoveEmptyEntries);

            foreach (string pair in pairs)
            {
                string[] kvp =
                    pair.Split(
                        '=',
                        2);

                if (kvp.Length != 2)
                {
                    continue;
                }

                string keyText =
                    Uri.UnescapeDataString(
                        kvp[0])
                        .Trim();

                if (!keyText.Equals(
                    key,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (long.TryParse(
                    Uri.UnescapeDataString(kvp[1]),
                    out long value))
                {
                    return value;
                }

                return null;
            }

            return null;
        }

        private static bool IsNexusDownloadHost(
            string host)
        {
            return host.Equals(
                "nexusmods.com",
                StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(
                    ".nexusmods.com",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static int? GetNexusQueryValue(
            string query,
            string key)
        {
            return GetNxmQueryValue(query, key);
        }

        private static long? GetNexusLongQueryValue(
            string query,
            string key)
        {
            return GetNxmLongQueryValue(query, key);
        }

        private Brush StatusBrush(
            bool isHealthy)
        {
            return (Brush)FindResource(
                isHealthy
                    ? "CyanBrush"
                    : "PinkBrush");
        }
    }
}
