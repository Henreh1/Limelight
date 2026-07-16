using System.Windows;

namespace Limelight.Views
{
    public partial class LiveLoaderInitializingWindow : Window
    {
        public LiveLoaderInitializingWindow()
        {
            InitializeComponent();
        }

        public void Report(
            string phase,
            int progress,
            string? detail = null)
        {
            PhaseText.Text = phase;

            InitialisationProgress.Value =
                Math.Clamp(
                    progress,
                    0,
                    100);

            if (!string.IsNullOrWhiteSpace(detail))
            {
                DetailText.Text = detail;
            }
        }
    }
}
