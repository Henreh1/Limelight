using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;

namespace Limelight.Animations
{
    public static class WindowMotion
    {
        public static readonly DependencyProperty AnimateCloseProperty =
            DependencyProperty.RegisterAttached(
                "AnimateClose",
                typeof(bool),
                typeof(WindowMotion),
                new PropertyMetadata(
                    false,
                    AnimateCloseChanged));

        private static readonly DependencyProperty CloseAnimationFinishedProperty =
            DependencyProperty.RegisterAttached(
                "CloseAnimationFinished",
                typeof(bool),
                typeof(WindowMotion),
                new PropertyMetadata(false));

        public static void SetAnimateClose(
            DependencyObject element,
            bool value)
        {
            element.SetValue(
                AnimateCloseProperty,
                value);
        }

        public static bool GetAnimateClose(
            DependencyObject element)
        {
            return (bool)element.GetValue(
                AnimateCloseProperty);
        }

        private static void AnimateCloseChanged(
            DependencyObject dependencyObject,
            DependencyPropertyChangedEventArgs e)
        {
            if (dependencyObject is not Window window)
            {
                return;
            }

            window.Closing -= Window_Closing;

            if (e.NewValue is true)
            {
                window.Closing += Window_Closing;
            }
        }

        private static void Window_Closing(
            object? sender,
            CancelEventArgs e)
        {
            if (sender is not Window window ||
                (bool)window.GetValue(
                    CloseAnimationFinishedProperty) ||
                !SystemParameters.ClientAreaAnimation ||
                Application.Current?.Dispatcher.HasShutdownStarted == true)
            {
                return;
            }

            // A modal window can close by assigning DialogResult. I remember
            // that value before delaying the close, otherwise WPF reports an
            // accepted choice as cancelled after the animation finishes.
            bool? requestedDialogResult = window.DialogResult;

            e.Cancel = true;
            window.IsHitTestVisible = false;

            // The card grows into view, then the whole popup leaves with a
            // short fade. This keeps every close path consistent, including X.
            DoubleAnimation fade =
                new()
                {
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(120),
                    EasingFunction =
                        new CubicEase
                        {
                            EasingMode = EasingMode.EaseIn
                        }
                };

            fade.Completed +=
                (_, _) =>
                {
                    window.SetValue(
                        CloseAnimationFinishedProperty,
                        true);

                    window.BeginAnimation(
                        UIElement.OpacityProperty,
                        null);

                    window.Opacity = 0;

                    if (requestedDialogResult.HasValue)
                    {
                        window.DialogResult =
                            requestedDialogResult.Value;
                    }
                    else
                    {
                        window.Close();
                    }
                };

            window.BeginAnimation(
                UIElement.OpacityProperty,
                fade,
                HandoffBehavior.SnapshotAndReplace);
        }
    }
}
