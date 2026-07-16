using Limelight.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Limelight.Views
{
    public partial class BrowseNexusPage : UserControl
    {
        private const int ModsPerPage = 12;

        public event Action<string>? SearchRequested;
        public event Action<string>? SortChanged;
        public event Action<string>? CategoryChanged;
        public event Action? RefreshRequested;
        public event Action<NexusModSummary>? ViewFilesRequested;

        private bool _isUpdatingCategories;

        private IReadOnlyList<NexusModSummary> _allMods =
            Array.Empty<NexusModSummary>();

        private int _currentPage = 1;

        public BrowseNexusPage()
        {
            InitializeComponent();
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
            bool isConnected)
        {
            NexusPageStatusBadgeText.Text =
                isConnected
                    ? "API READY"
                    : "ACCOUNT REQUIRED";

            NexusPageStatusBadgeText.Foreground =
                StatusBrush(isConnected);

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

            // Limelight keeps every matching result available, then builds
            // only the current page of cards so a large catalogue stays fast.
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
            // Returning to the result heading keeps the next page from
            // appearing halfway down the window after a long card grid.
            NexusResultsScrollViewer.ScrollToVerticalOffset(
                0);
        }

        private void ViewFilesButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.Tag is NexusModSummary mod)
            {
                ViewFilesRequested?.Invoke(mod);
            }
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
