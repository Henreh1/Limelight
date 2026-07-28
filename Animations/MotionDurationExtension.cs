using System;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media.Animation;

namespace Limelight.Animations
{
    [MarkupExtensionReturnType(typeof(Duration))]
    public sealed class MotionDurationExtension : MarkupExtension
    {
        public double Milliseconds { get; set; }

        public override object ProvideValue(
            IServiceProvider serviceProvider)
        {
            // I follow Windows' animation preference here so Limelight's
            // movement can be removed without maintaining a separate toggle.
            TimeSpan duration =
                SystemParameters.ClientAreaAnimation
                    ? TimeSpan.FromMilliseconds(
                        Math.Max(0, Milliseconds))
                    : TimeSpan.Zero;

            return new Duration(duration);
        }
    }
}
