using Limelight.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Limelight.Views
{
    public partial class NexusModFilesView : UserControl
    {
        public event Action? BackRequested;
        public event Action<NexusModFile>? DownloadRequested;

        public NexusModSummary? SelectedMod { get; private set; }

        public NexusModFilesView()
        {
            InitializeComponent();

            BackToModButton.Click +=
                (_, _) => BackRequested?.Invoke();

            FilesList.AddHandler(
                Button.ClickEvent,
                new RoutedEventHandler(
                    FilesListButton_Click));
        }

        public void ShowLoading(
            NexusModSummary mod)
        {
            ArgumentNullException.ThrowIfNull(mod);

            SelectedMod =
                mod;

            ShowModHeader(mod);

            LoadingPanel.Visibility =
                Visibility.Visible;

            EmptyPanel.Visibility =
                Visibility.Collapsed;

            ErrorPanel.Visibility =
                Visibility.Collapsed;

            FilesScrollViewer.Visibility =
                Visibility.Collapsed;

            FooterStatusText.Text =
                "Loading the available Nexus files.";
        }

        public void ShowFiles(
            NexusModSummary mod,
            IEnumerable<NexusModFile> files)
        {
            ArgumentNullException.ThrowIfNull(mod);
            ArgumentNullException.ThrowIfNull(files);

            SelectedMod =
                mod;

            ShowModHeader(mod);

            List<NexusModFile> visibleFiles =
                files
                    .Where(file =>
                        file.CategoryId != 6 &&
                        file.CategoryId != 7)
                    .OrderBy(file =>
                        file.DisplayPriority)
                    .ThenByDescending(file =>
                        file.IsPrimary)
                    .ThenByDescending(file =>
                        file.UploadedTimestamp)
                    .ToList();

            LoadingPanel.Visibility =
                Visibility.Collapsed;

            ErrorPanel.Visibility =
                Visibility.Collapsed;

            FileCountText.Text =
                visibleFiles.Count == 1
                    ? "1 FILE"
                    : $"{visibleFiles.Count:N0} FILES";

            if (visibleFiles.Count == 0)
            {
                FilesScrollViewer.Visibility =
                    Visibility.Collapsed;

                EmptyPanel.Visibility =
                    Visibility.Visible;

                FooterStatusText.Text =
                    "No downloadable files were returned by Nexus.";

                return;
            }

            EmptyPanel.Visibility =
                Visibility.Collapsed;

            FilesList.ItemsSource =
                visibleFiles;

            FilesScrollViewer.Visibility =
                Visibility.Visible;

            FilesScrollViewer.ScrollToTop();

            FooterStatusText.Text =
                visibleFiles.Count == 1
                    ? "1 downloadable file is available."
                    : $"{visibleFiles.Count:N0} downloadable files are available.";
        }

        public void ShowError(
            string message)
        {
            LoadingPanel.Visibility =
                Visibility.Collapsed;

            EmptyPanel.Visibility =
                Visibility.Collapsed;

            FilesScrollViewer.Visibility =
                Visibility.Collapsed;

            ErrorText.Text =
                message;

            ErrorPanel.Visibility =
                Visibility.Visible;

            FooterStatusText.Text =
                "The Nexus file list could not be loaded.";
        }

        public void ShowDownloadState(
            NexusModFile file,
            string message,
            bool isBusy,
            int? percentage = null)
        {
            ArgumentNullException.ThrowIfNull(file);

            FilesList.IsEnabled =
                !isBusy;

            DownloadProgressBar.Visibility =
                isBusy
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            DownloadProgressBar.IsIndeterminate =
                isBusy &&
                percentage is null;

            if (percentage is >= 0)
            {
                DownloadProgressBar.Value =
                    Math.Clamp(
                        percentage.Value,
                        0,
                        100);
            }

            FooterStatusText.Text =
                percentage is >= 0
                    ? $"{message} {percentage.Value}%"
                    : message;
        }

        private void ShowModHeader(
            NexusModSummary mod)
        {
            ModNameText.Text =
                mod.Name;

            ModDetailText.Text =
                $"BY {mod.Author.ToUpperInvariant()}  •  " +
                $"MOD {mod.ModId}  •  " +
                $"VERSION {mod.Version}";

            LoadModPicture(
                mod.PictureUrl);
        }

        private void FilesListButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Button? button =
                FindVisualParent<Button>(
                    e.OriginalSource as DependencyObject);

            if (button?.Name != "DownloadFileButton" ||
                button.Tag is not NexusModFile file)
            {
                return;
            }

            DownloadRequested?.Invoke(
                file);

            e.Handled =
                true;
        }

        private void LoadModPicture(
            string pictureUrl)
        {
            ModPicture.Source =
                null;

            if (!Uri.TryCreate(
                    pictureUrl,
                    UriKind.Absolute,
                    out Uri? imageUri))
            {
                return;
            }

            try
            {
                BitmapImage image =
                    new();

                image.BeginInit();
                image.CacheOption =
                    BitmapCacheOption.OnLoad;

                image.UriSource =
                    imageUri;

                image.EndInit();
                image.Freeze();

                ModPicture.Source =
                    image;
            }
            catch
            {
                // The Limelight letter remains visible when Nexus has no usable image.
            }
        }

        private static T? FindVisualParent<T>(
            DependencyObject? child)
            where T : DependencyObject
        {
            DependencyObject? current =
                child;

            while (current is not null)
            {
                if (current is T match)
                {
                    return match;
                }

                current =
                    VisualTreeHelper.GetParent(
                        current);
            }

            return null;
        }
    }
}
