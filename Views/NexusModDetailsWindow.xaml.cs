using Limelight.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Limelight.Views
{
    public partial class NexusModDetailsWindow : Window
    {
        private static readonly Regex DescriptionImageExpression =
            new(
                @"\[img\](?<bbcode>.*?)\[/img\]|" +
                @"<img[^>]+src\s*=\s*[""'](?<html>[^""']+)[""'][^>]*>",
                RegexOptions.IgnoreCase |
                RegexOptions.Singleline);

        public event Action<NexusModSummary>? ViewFilesRequested;

        public NexusModSummary SelectedMod { get; }

        public NexusModDetailsWindow(
            NexusModSummary mod)
        {
            ArgumentNullException.ThrowIfNull(mod);

            SelectedMod =
                mod;

            InitializeComponent();

            CloseButton.Click +=
                CloseButton_Click;

            ErrorCloseButton.Click +=
                CloseButton_Click;

            ViewFilesButton.Click +=
                ViewFilesButton_Click;

            OpenNexusButton.Click +=
                OpenNexusButton_Click;

            TitleBar.MouseLeftButtonDown +=
                TitleBar_MouseLeftButtonDown;

            ShowModHeader();
            ShowLoading();
        }

        public void ShowLoading()
        {
            LoadingPanel.Visibility =
                Visibility.Visible;

            ErrorPanel.Visibility =
                Visibility.Collapsed;

            DetailsScrollViewer.Visibility =
                Visibility.Collapsed;
        }

        public void ShowDetails(
            string description,
            IEnumerable<string>? requirements)
        {
            DescriptionContentPanel.Children.Clear();

            AddDescriptionContent(
                description);

            List<string> requirementList =
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
                requirementList;

            RequirementsList.Visibility =
                requirementList.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            NoRequirementsText.Visibility =
                requirementList.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;

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

        private void ShowModHeader()
        {
            ModNameText.Text =
                SelectedMod.Name;

            ModMetaText.Text =
                $"BY {SelectedMod.Author.ToUpperInvariant()}  •  " +
                $"VERSION {SelectedMod.Version}";

            CategoryText.Text =
                SelectedMod.CategoryName.ToUpperInvariant();

            SummaryText.Text =
                SelectedMod.Summary;

            DownloadCountText.Text =
                SelectedMod.TotalDownloads.ToString("N0");

            EndorsementCountText.Text =
                SelectedMod.Endorsements.ToString("N0");

            ModIdText.Text =
                SelectedMod.ModId.ToString();

            LoadHeroImage(
                SelectedMod.PictureUrl);
        }

        private void AddDescriptionContent(
            string description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                AddDescriptionText(
                    "The author has not provided a full description.");

                return;
            }

            int previousEnd =
                0;

            MatchCollection imageMatches =
                DescriptionImageExpression.Matches(
                    description);

            foreach (Match match in imageMatches)
            {
                string textBeforeImage =
                    description.Substring(
                        previousEnd,
                        match.Index - previousEnd);

                AddDescriptionText(
                    textBeforeImage);

                string imageUrl =
                    match.Groups["bbcode"].Success
                        ? match.Groups["bbcode"].Value
                        : match.Groups["html"].Value;

                AddDescriptionImage(
                    WebUtility.HtmlDecode(
                        imageUrl.Trim()));

                previousEnd =
                    match.Index +
                    match.Length;
            }

            if (previousEnd < description.Length)
            {
                AddDescriptionText(
                    description[previousEnd..]);
            }

            if (DescriptionContentPanel.Children.Count == 0)
            {
                AddDescriptionText(
                    description);
            }
        }

        private void AddDescriptionText(
            string text)
        {
            string cleanedText =
                CleanDescriptionText(
                    text);

            if (string.IsNullOrWhiteSpace(cleanedText))
            {
                return;
            }

            DescriptionContentPanel.Children.Add(
                new TextBlock
                {
                    Text = cleanedText,
                    Margin =
                        new Thickness(0, 0, 0, 18),
                    FontSize = 13,
                    LineHeight = 21,
                    TextWrapping =
                        TextWrapping.Wrap,
                    Foreground =
                        (Brush)FindResource(
                            "MutedTextBrush")
                });
        }

        private void AddDescriptionImage(
            string imageUrl)
        {
            if (!Uri.TryCreate(
                    imageUrl,
                    UriKind.Absolute,
                    out Uri? imageUri))
            {
                return;
            }

            try
            {
                BitmapImage imageSource =
                    new BitmapImage();

                imageSource.BeginInit();

                imageSource.UriSource =
                    imageUri;

                imageSource.CacheOption =
                    BitmapCacheOption.OnDemand;

                imageSource.CreateOptions =
                    BitmapCreateOptions.IgnoreImageCache;

                imageSource.EndInit();

                var image =
                    new Image
                    {
                        Source = imageSource,
                        MaxHeight = 560,
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment =
                            HorizontalAlignment.Left
                    };

                DescriptionContentPanel.Children.Add(
                    new Border
                    {
                        Child = image,
                        Margin =
                            new Thickness(0, 0, 0, 20),
                        Background =
                            (Brush)FindResource(
                                "RaisedPanelBrush"),
                        BorderBrush =
                            (Brush)FindResource(
                                "BorderBrush"),
                        BorderThickness =
                            new Thickness(1),
                        CornerRadius =
                            new CornerRadius(7),
                        ClipToBounds = true
                    });
            }
            catch
            {
                // A broken description image should not hide the rest of the page.
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
                    @"</p\s*>",
                    "\n\n",
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
                    @"(\r?\n){3,}",
                    "\n\n");

            return cleaned.Trim();
        }

        private void LoadHeroImage(
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
                HeroImage.Source =
                    new BitmapImage(
                        imageUri);
            }
            catch
            {
                // Limelight's letter remains visible when an image is unavailable.
            }
        }

        private void ViewFilesButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ViewFilesRequested?.Invoke(
                SelectedMod);
        }

        private void OpenNexusButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            string modUrl =
                "https://www.nexusmods.com/deadasdisco/mods/" +
                SelectedMod.ModId;

            Process.Start(
                new ProcessStartInfo(modUrl)
                {
                    UseShellExecute = true
                });
        }

        private void TitleBar_MouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (e.LeftButton !=
                MouseButtonState.Pressed)
            {
                return;
            }

            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // Windows can release the mouse just before dragging begins.
            }
        }

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }
    }
}