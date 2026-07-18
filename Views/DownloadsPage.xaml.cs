using Limelight.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Limelight.Views
{
    public partial class DownloadsPage : UserControl
    {
        public event Action? ClearFinishedRequested;

        public DownloadsPage()
        {
            InitializeComponent();
        }

        public void ShowDownloads(
            IEnumerable<NexusDownloadRecord> downloads)
        {
            List<NexusDownloadRecord> visibleDownloads =
                downloads.ToList();

            int activeCount =
                visibleDownloads.Count(download => download.IsActive);

            DownloadActivityList.ItemsSource =
                null;

            DownloadActivityList.ItemsSource =
                visibleDownloads;

            EmptyState.Visibility =
                visibleDownloads.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            DownloadActivityScrollViewer.Visibility =
                visibleDownloads.Count == 0
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            ClearFinishedButton.IsEnabled =
                visibleDownloads.Any(download => !download.IsActive);

            DownloadCountText.Text =
                activeCount > 0
                    ? activeCount == 1
                        ? "1 ACTIVE"
                        : $"{activeCount} ACTIVE"
                    : visibleDownloads.Count == 0
                        ? "NO DOWNLOADS"
                        : visibleDownloads.Count == 1
                            ? "1 RECENT"
                            : $"{visibleDownloads.Count} RECENT";

            DownloadCountText.Foreground =
                (Brush)FindResource(
                    visibleDownloads.Count == 0
                        ? "PinkBrush"
                        : "CyanBrush");
        }

        private void ClearFinished_Click(
            object sender,
            RoutedEventArgs e)
        {
            ClearFinishedRequested?.Invoke();
        }
    }
}
