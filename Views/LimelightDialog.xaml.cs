using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Limelight.Views
{
    public enum LimelightDialogTone
    {
        Information,
        Success,
        Warning,
        Error,
        Question
    }

    public enum LimelightDialogChoice
    {
        Primary,
        Secondary,
        Cancelled
    }

    public partial class LimelightDialog : Window
    {
        public LimelightDialogChoice Choice { get; private set; } =
            LimelightDialogChoice.Cancelled;

        public LimelightDialog(
            string heading,
            string message,
            LimelightDialogTone tone,
            string primaryAction = "OK",
            string? secondaryAction = null,
            string? details = null,
            string? eyebrow = null,
            string? footerHint = null,
            bool showCancel = false)
        {
            InitializeComponent();

            HeadingText.Text = heading;
            MessageText.Text = message;
            PrimaryActionButton.Content = primaryAction;

            if (!string.IsNullOrWhiteSpace(secondaryAction))
            {
                SecondaryActionButton.Content = secondaryAction;
                SecondaryActionButton.Visibility = Visibility.Visible;
            }

            if (!string.IsNullOrWhiteSpace(details))
            {
                DetailText.Text = details;
                DetailPanel.Visibility = Visibility.Visible;
            }

            if (!string.IsNullOrWhiteSpace(footerHint))
            {
                FooterHintText.Text = footerHint;
            }

            CancelActionButton.Visibility =
                showCancel
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            ApplyTone(tone, eyebrow);

            PrimaryActionButton.Click += (_, _) =>
                CloseWith(LimelightDialogChoice.Primary);

            SecondaryActionButton.Click += (_, _) =>
                CloseWith(LimelightDialogChoice.Secondary);

            CancelActionButton.Click += (_, _) =>
                CloseWith(LimelightDialogChoice.Cancelled);

            CloseButton.Click += (_, _) =>
                CloseWith(LimelightDialogChoice.Cancelled);

            TitleBar.MouseLeftButtonDown += (_, eventArgs) =>
            {
                if (eventArgs.ChangedButton == MouseButton.Left)
                {
                    DragMove();
                }
            };

            KeyDown += Window_KeyDown;
            Loaded += (_, _) => PrimaryActionButton.Focus();
        }

        public static LimelightDialogChoice Open(
            Window? owner,
            string heading,
            string message,
            LimelightDialogTone tone = LimelightDialogTone.Information,
            string primaryAction = "OK",
            string? secondaryAction = null,
            string? details = null,
            string? eyebrow = null,
            string? footerHint = null,
            bool showCancel = false)
        {
            var dialog =
                new LimelightDialog(
                    heading,
                    message,
                    tone,
                    primaryAction,
                    secondaryAction,
                    details,
                    eyebrow,
                    footerHint,
                    showCancel);

            if (owner != null)
            {
                dialog.Owner = owner;
            }

            dialog.ShowDialog();
            return dialog.Choice;
        }

        private void ApplyTone(
            LimelightDialogTone tone,
            string? customEyebrow)
        {
            bool isPositive =
                tone == LimelightDialogTone.Information ||
                tone == LimelightDialogTone.Success;

            Brush accent =
                (Brush)FindResource(
                    isPositive
                        ? "CyanBrush"
                        : "PinkBrush");

            ToneIconBorder.BorderBrush = accent;
            ToneIconText.Foreground = accent;
            TitleBarDiamond.Foreground = accent;
            EyebrowText.Foreground = accent;
            AccentLine.Fill = accent;
            PrimaryActionButton.Background = accent;

            ToneIconText.Text = tone switch
            {
                LimelightDialogTone.Success => "\u2713",
                LimelightDialogTone.Warning => "!",
                LimelightDialogTone.Error => "\u00D7",
                LimelightDialogTone.Question => "?",
                _ => "i"
            };

            EyebrowText.Text =
                customEyebrow ??
                tone switch
                {
                    LimelightDialogTone.Success => "ALL SET",
                    LimelightDialogTone.Warning => "NEEDS ATTENTION",
                    LimelightDialogTone.Error => "SOMETHING MISSED ITS CUE",
                    LimelightDialogTone.Question => "YOUR CALL",
                    _ => "LIMELIGHT"
                };
        }

        private void Window_KeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CloseWith(LimelightDialogChoice.Cancelled);
            }
            else if (e.Key == Key.Enter)
            {
                CloseWith(LimelightDialogChoice.Primary);
            }
        }

        private void CloseWith(
            LimelightDialogChoice choice)
        {
            Choice = choice;
            Close();
        }
    }
}
