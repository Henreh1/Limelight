using Limelight.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Limelight.Views
{
    public partial class NexusModDetailsView : UserControl
    {
        private static readonly Regex DescriptionImageExpression =
            new(
                @"\[img(?:=[^\]]+|\s+[^\]]*)?\](?<bbcode>.*?)\[/img\]|" +
                @"<img[^>]+src\s*=\s*[""'](?<html>[^""']+)[""'][^>]*>",
                RegexOptions.IgnoreCase |
                RegexOptions.Singleline);

        private readonly List<string> _galleryImages =
            new();

        private int _currentImageIndex;

        public event Action? BackRequested;
        public event Action<NexusModSummary>? ViewFilesRequested;

        public NexusModSummary? SelectedMod { get; private set; }

        public NexusModDetailsView()
        {
            InitializeComponent();

            BackToSearchButton.Click +=
                (_, _) => BackRequested?.Invoke();

            ViewFilesButton.Click +=
                ViewFilesButton_Click;

            OpenNexusButton.Click +=
                (_, _) => OpenSelectedModPage(showGallery: false);

            OpenGalleryButton.Click +=
                (_, _) => OpenSelectedModPage(showGallery: true);

            PreviousImageButton.Click +=
                (_, _) => ChangeGalleryImage(-1);

            NextImageButton.Click +=
                (_, _) => ChangeGalleryImage(1);

            CarouselThumbnailsList.AddHandler(
                Button.ClickEvent,
                new RoutedEventHandler(
                    CarouselThumbnailButton_Click));
        }

        public void ShowLoading(
            NexusModSummary mod)
        {
            ArgumentNullException.ThrowIfNull(mod);

            SelectedMod =
                mod;

            ShowModHeader(mod);

            DetailsScrollViewer.Visibility =
                Visibility.Collapsed;

            ErrorPanel.Visibility =
                Visibility.Collapsed;

            LoadingPanel.Visibility =
                Visibility.Visible;
        }

        public void ShowDetails(
            NexusModSummary mod,
            IEnumerable<string>? requirements = null)
        {
            ArgumentNullException.ThrowIfNull(mod);

            SelectedMod =
                mod;

            ShowModHeader(mod);
            BuildGallery(mod);
            ShowDescription(mod.Description);
            ShowRequirements(requirements);

            LoadingPanel.Visibility =
                Visibility.Collapsed;

            ErrorPanel.Visibility =
                Visibility.Collapsed;

            DetailsScrollViewer.Visibility =
                Visibility.Visible;

            DetailsScrollViewer.ScrollToTop();
        }

        public void ShowError(
            string message)
        {
            LoadingPanel.Visibility =
                Visibility.Collapsed;

            DetailsScrollViewer.Visibility =
                Visibility.Collapsed;

            ErrorText.Text =
                message;

            ErrorPanel.Visibility =
                Visibility.Visible;
        }

        private void ShowModHeader(
            NexusModSummary mod)
        {
            ModNameText.Text =
                mod.Name;

            ModMetaText.Text =
                $"BY {mod.Author.ToUpperInvariant()}  •  " +
                $"VERSION {mod.Version}";

            CategoryText.Text =
                mod.CategoryName.ToUpperInvariant();

            SummaryText.Text =
                mod.Summary;

            DownloadCountText.Text =
                mod.TotalDownloads.ToString("N0");

            EndorsementCountText.Text =
                mod.Endorsements.ToString("N0");

            ModIdText.Text =
                mod.ModId.ToString();
        }

        private void BuildGallery(
            NexusModSummary mod)
        {
            _galleryImages.Clear();

            AddGalleryImage(
                mod.PictureUrl);

            foreach (Match match in
                     DescriptionImageExpression.Matches(
                         mod.Description ?? string.Empty))
            {
                string imageUrl =
                    match.Groups["bbcode"].Success
                        ? match.Groups["bbcode"].Value
                        : match.Groups["html"].Value;

                AddGalleryImage(
                    imageUrl);
            }

            CarouselThumbnailsList.ItemsSource =
                _galleryImages.ToList();

            bool hasMoreThanOneImage =
                _galleryImages.Count > 1;

            CarouselPanel.Visibility =
                hasMoreThanOneImage
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            PreviousImageButton.Visibility =
                hasMoreThanOneImage
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            NextImageButton.Visibility =
                PreviousImageButton.Visibility;

            if (_galleryImages.Count == 0)
            {
                HeroImage.Source =
                    null;

                ImageCounterText.Text =
                    "NO IMAGES";

                return;
            }

            ShowGalleryImage(0);
        }

        private void AddGalleryImage(
            string? imageUrl)
        {
            string decodedUrl =
                WebUtility.HtmlDecode(
                    imageUrl?.Trim() ?? string.Empty);

            if (!Uri.TryCreate(
                    decodedUrl,
                    UriKind.Absolute,
                    out _) ||
                _galleryImages.Contains(
                    decodedUrl,
                    StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            _galleryImages.Add(
                decodedUrl);
        }

        private void ChangeGalleryImage(
            int direction)
        {
            if (_galleryImages.Count <= 1)
            {
                return;
            }

            int nextIndex =
                (_currentImageIndex + direction + _galleryImages.Count) %
                _galleryImages.Count;

            ShowGalleryImage(nextIndex);
        }

        private void ShowGalleryImage(
    int imageIndex)
        {
            if (imageIndex < 0 ||
                imageIndex >= _galleryImages.Count)
            {
                return;
            }

            _currentImageIndex =
                imageIndex;

            ImageCounterText.Text =
                $"{imageIndex + 1} / {_galleryImages.Count}";

            try
            {
                BitmapImage image =
                    new();

                image.BeginInit();

                // Nexus images load over the network, so WPF should
                // be allowed to finish downloading them in the background.
                image.CacheOption =
                    BitmapCacheOption.OnDemand;

                image.CreateOptions =
                    BitmapCreateOptions.IgnoreImageCache;

                image.UriSource =
                    new Uri(
                        _galleryImages[imageIndex],
                        UriKind.Absolute);

                image.EndInit();

                HeroImage.Source =
                    image;
            }
            catch
            {
                // Keep the Limelight placeholder visible if Nexus cannot supply this image.
                HeroImage.Source =
                    null;
            }
        }

        private void CarouselThumbnailButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Button? button =
                FindVisualParent<Button>(
                    e.OriginalSource as DependencyObject);

            if (button?.Tag is not string imageUrl)
            {
                return;
            }

            int imageIndex =
                _galleryImages.FindIndex(
                    candidate =>
                        candidate.Equals(
                            imageUrl,
                            StringComparison.OrdinalIgnoreCase));

            ShowGalleryImage(imageIndex);

            e.Handled =
                true;
        }

        private void ShowDescription(
            string description)
        {
            DescriptionContentPanel.Children.Clear();

            string descriptionWithoutImages =
                DescriptionImageExpression.Replace(
                    description ?? string.Empty,
                    string.Empty);

            string cleanedDescription =
                CleanDescriptionText(
                    descriptionWithoutImages);

            if (string.IsNullOrWhiteSpace(cleanedDescription))
            {
                cleanedDescription =
                    "The author has not provided a full description.";
            }

            string[] paragraphs =
                Regex.Split(
                    cleanedDescription,
                    @"(?:\r?\n){2,}");

            foreach (string paragraph in paragraphs)
            {
                if (string.IsNullOrWhiteSpace(paragraph))
                {
                    continue;
                }

                DescriptionContentPanel.Children.Add(
                    new TextBlock
                    {
                        Text = paragraph.Trim(),
                        Margin =
                            new Thickness(0, 0, 0, 17),
                        FontSize = 13,
                        LineHeight = 21,
                        TextWrapping =
                            TextWrapping.Wrap,
                        Foreground =
                            (Brush)FindResource(
                                "MutedTextBrush")
                    });
            }
        }

        private static string CleanDescriptionText(
            string text)
        {
            string cleaned =
                WebUtility.HtmlDecode(text);

            cleaned =
                Regex.Replace(
                    cleaned,
                    @"<br\s*/?>",
                    "\n",
                    RegexOptions.IgnoreCase);

            cleaned =
                Regex.Replace(
                    cleaned,
                    @"</(?:p|div|h[1-6]|li)\s*>",
                    "\n\n",
                    RegexOptions.IgnoreCase);

            cleaned =
                Regex.Replace(
                    cleaned,
                    @"<li[^>]*>",
                    "• ",
                    RegexOptions.IgnoreCase);

            cleaned =
                Regex.Replace(
                    cleaned,
                    @"<[^>]+>",
                    string.Empty);

            cleaned =
                Regex.Replace(
                    cleaned,
                    @"\[url=[^\]]+\](.*?)\[/url\]",
                    "$1",
                    RegexOptions.IgnoreCase |
                    RegexOptions.Singleline);

            cleaned =
                Regex.Replace(
                    cleaned,
                    @"\[\*\]",
                    "• ",
                    RegexOptions.IgnoreCase);

            cleaned =
                Regex.Replace(
                    cleaned,
                    @"\[/(?:size|heading|h[1-6]|list|center|left|right)\]",
                    "\n\n",
                    RegexOptions.IgnoreCase);

            cleaned =
                Regex.Replace(
                    cleaned,
                    @"\[/?[^\]]+\]",
                    string.Empty);

            cleaned =
                Regex.Replace(
                    cleaned,
                    @"[ \t]+\r?\n",
                    "\n");

            cleaned =
                Regex.Replace(
                    cleaned,
                    @"(?:\r?\n){3,}",
                    "\n\n");

            return cleaned.Trim();
        }

        private void ShowRequirements(
            IEnumerable<string>? requirements)
        {
            List<string> visibleRequirements =
                requirements?
                    .Where(requirement =>
                        !string.IsNullOrWhiteSpace(requirement))
                    .Select(requirement =>
                        requirement.Trim())
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .ToList() ??
                new List<string>();

            RequirementsList.ItemsSource =
                visibleRequirements;

            RequirementsList.Visibility =
                visibleRequirements.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            NoRequirementsText.Visibility =
                visibleRequirements.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private void ViewFilesButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (SelectedMod is not null)
            {
                ViewFilesRequested?.Invoke(
                    SelectedMod);
            }
        }

        private void OpenSelectedModPage(
            bool showGallery)
        {
            if (SelectedMod is null)
            {
                return;
            }

            string modUrl =
                "https://www.nexusmods.com/deadasdisco/mods/" +
                SelectedMod.ModId;

            if (showGallery)
            {
                modUrl += "?tab=images";
            }

            Process.Start(
                new ProcessStartInfo(modUrl)
                {
                    UseShellExecute = true
                });
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
