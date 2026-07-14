using System.Windows;
using System.Windows.Input;

namespace Limelight.Views
{
    public partial class LiveLoaderSetupWindow : Window
    {
        public bool SetupRequested { get; private set; }

        public bool PromptDismissed { get; private set; }

        public LiveLoaderSetupWindow()
        {
            InitializeComponent();
        }

        private void Setup_Click(
            object sender,
            RoutedEventArgs e)
        {
            SetupRequested = true;

            // DialogResult also closes the popup and returns control to the
            // main window that opened it.
            DialogResult = true;
        }

        private void NotNow_Click(
            object sender,
            RoutedEventArgs e)
        {
            PromptDismissed = true;

            // Remembering this choice will prevent the popup from appearing
            // every time Limelight checks the game folder.
            DialogResult = false;
        }

        private void Window_MouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                // The custom window has no normal title bar, so allow the user
                // to drag it from anywhere that is not a button.
                DragMove();
            }
        }
    }
}