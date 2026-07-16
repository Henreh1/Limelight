using Limelight.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Limelight.Views
{
    public partial class NexusModFilesWindow : Window
    {
        public event Action<NexusModFile>? DownloadRequested;

        public NexusModSummary SelectedMod { get; }

        public NexusModFilesWindow(
            NexusModSummary mod)
        {
            ArgumentNullException.ThrowIfNull(mod);

            SelectedMod =
                mod;

            InitializeComponent();

            ModNameText.Text =
                mod.Name;

            ModDetailText.Text =
                $"BY {mod.Author.ToUpperInvariant()}  •  " +
                $"MOD {mod.ModId}  •  " +
                $"VERSION {mod.Version}";

            CloseButton.Click +=
                CloseButton_Click;

            CancelButton.Click +=
                CloseButton_Click;

            TitleBar.MouseLeftButtonDown +=
                TitleBar_MouseLeftButtonDown;

            FilesList.AddHandler(
                Button.ClickEvent,
                new RoutedEventHandler(
                    FilesListButton_Click));

            LoadModPicture(
                mod.PictureUrl);

            ShowLoading();
        }

        public void ShowLoading()
        {
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
            IEnumerable<NexusModFile> files)
        {
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

            FooterStatusText.Text =
                visibleFiles.Count == 1
                    ? "1 downloadable file is available."
                    : $"{visibleFiles.Count} downloadable files are available.";
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

        private void FilesListButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (e.OriginalSource is not Button button ||
                button.Name != "DownloadFileButton" ||
                button.Tag is not NexusModFile file)
            {
                return;
            }

            DownloadRequested?.Invoke(
                file);

            e.Handled =
                true;
        }

        private void TitleBar_MouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // Windows can release the mouse just before DragMove starts.
            }
        }

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }

        private void LoadModPicture(
            string pictureUrl)
        {
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
                    new BitmapImage();

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
                // The Limelight letter remains visible if Nexus has no usable image.
            }
        }
    }
}