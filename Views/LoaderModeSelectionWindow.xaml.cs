using Limelight.Models;
using System.Windows;
using System.Windows.Input;

namespace Limelight.Views
{
    public partial class LoaderModeSelectionWindow : Window
    {
        private readonly bool _hasX19Group;
        private readonly bool _liveLoaderAvailable;

        public LoaderLaunchMode? SelectedMode { get; private set; }

        public bool ConfigureX19Requested { get; private set; }

        public bool OpenSupportRequested { get; private set; }

        public LoaderModeSelectionWindow(
            int x19ModCount,
            string hotkeyGesture,
            LocalCompatibilityResult compatibility)
        {
            InitializeComponent();

            _hasX19Group =
                x19ModCount > 0;

            _liveLoaderAvailable =
                compatibility.IsLiveLoaderCompatible;

            X19CountText.Text =
                x19ModCount == 1
                    ? "1 MOD"
                    : $"{x19ModCount} MODS";

            X19DescriptionText.Text =
                _hasX19Group
                    ? $"Cycle through your selected character group with {hotkeyGesture.ToUpperInvariant()}."
                    : "No characters are selected for the X19 rotation yet.";

            X19ActionText.Text =
                _hasX19Group
                    ? "SELECT X19"
                    : "GROUP REQUIRED";

            NormalLoaderButton.Opacity =
                _liveLoaderAvailable ? 1 : 0.72;

            X19LoaderButton.Opacity =
                _liveLoaderAvailable ? 1 : 0.72;

            if (!_liveLoaderAvailable)
            {
                CompatibilityPromptText.Text =
                    compatibility.Detail;

                CompatibilityPrompt.Visibility =
                    Visibility.Visible;
            }
        }

        private void NormalLoader_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!_liveLoaderAvailable)
            {
                // I send the user straight to Limelight's repair tools instead
                // of leaving a card on screen that appears to do nothing.
                OpenSupportRequested = true;
                DialogResult = false;
                return;
            }

            SelectedMode =
                LoaderLaunchMode.Normal;

            DialogResult = true;
        }

        private void X19Loader_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!_liveLoaderAvailable)
            {
                // X19 needs the same verified bridge as the normal loader.
                OpenSupportRequested = true;
                DialogResult = false;
                return;
            }

            if (!_hasX19Group)
            {
                EmptyGroupPrompt.Visibility =
                    Visibility.Visible;

                return;
            }

            SelectedMode =
                LoaderLaunchMode.X19;

            DialogResult = true;
        }

        private void NoLiveLoader_Click(
            object sender,
            RoutedEventArgs e)
        {
            SelectedMode =
                LoaderLaunchMode.Disabled;

            DialogResult = true;
        }

        private void ConfigureX19_Click(
            object sender,
            RoutedEventArgs e)
        {
            ConfigureX19Requested = true;
            DialogResult = false;
        }

        private void OpenSupport_Click(
            object sender,
            RoutedEventArgs e)
        {
            OpenSupportRequested = true;
            DialogResult = false;
        }

        private void Cancel_Click(
            object sender,
            RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void TitleBar_MouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }
    }
}
