using Limelight.Models;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Limelight.Views
{
    public partial class PrivateTestReportWindow : Window
    {
        private readonly List<string> _attachmentPaths =
            new List<string>();

        public PrivateTestReportRequest? ReportRequest { get; private set; }

        public PrivateTestReportWindow()
        {
            InitializeComponent();
        }

        private void AddFiles_Click(
            object sender,
            RoutedEventArgs e)
        {
            IReadOnlyList<string> selectedFiles =
                LimelightFilePickerWindow.PickFiles(
                    this,
                    "Add evidence to the Limelight test report",
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.DesktopDirectory),
                    new[]
                    {
                        ".png", ".jpg", ".jpeg", ".webp", ".dmp",
                        ".log", ".txt", ".json", ".ini", ".cfg",
                        ".xml", ".csv"
                    },
                    "SUPPORTED EVIDENCE");

            if (selectedFiles.Count == 0)
            {
                return;
            }

            foreach (string path in selectedFiles)
            {
                if (!_attachmentPaths.Contains(
                        path,
                        StringComparer.OrdinalIgnoreCase) &&
                    _attachmentPaths.Count < 10)
                {
                    _attachmentPaths.Add(path);
                }
            }

            AttachmentSummaryText.Text =
                _attachmentPaths.Count == 0
                    ? "No screenshots, logs, or crash dumps selected."
                    : _attachmentPaths.Count == 1
                        ? $"1 file selected: {Path.GetFileName(_attachmentPaths[0])}"
                        : $"{_attachmentPaths.Count} files selected. Original folder paths will not be included.";
        }

        private void Create_Click(
            object sender,
            RoutedEventArgs e)
        {
            string summary =
                SummaryBox.Text.Trim();

            string actualResult =
                ActualBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(summary) ||
                string.IsNullOrWhiteSpace(actualResult))
            {
                ValidationText.Text =
                    "ADD AN ISSUE TITLE AND TELL US WHAT ACTUALLY HAPPENED.";

                ValidationText.Visibility =
                    Visibility.Visible;

                return;
            }

            ReportRequest =
                new PrivateTestReportRequest
                {
                    Summary = summary,
                    Area = SelectedText(AreaBox),
                    ReproductionSteps = StepsBox.Text.Trim(),
                    ExpectedResult = ExpectedBox.Text.Trim(),
                    ActualResult = actualResult,
                    Outcome = SelectedText(OutcomeBox),
                    AttachmentPaths = new List<string>(_attachmentPaths)
                };

            DialogResult = true;
        }

        private static string SelectedText(
            ComboBox comboBox)
        {
            return comboBox.SelectedItem is ComboBoxItem item
                ? item.Content?.ToString() ?? string.Empty
                : comboBox.Text;
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
