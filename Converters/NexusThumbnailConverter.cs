using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Limelight.Converters
{
    public sealed class NexusThumbnailConverter : IValueConverter
    {
        public int DecodePixelWidth { get; set; } =
            420;

        public object Convert(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture)
        {
            if (value is not string imageUrl ||
                !Uri.TryCreate(
                    imageUrl,
                    UriKind.Absolute,
                    out Uri? imageUri))
            {
                return DependencyProperty.UnsetValue;
            }

            try
            {
                BitmapImage thumbnail =
                    new();

                thumbnail.BeginInit();

                // I decode catalogue artwork close to the size Limelight
                // actually draws. This avoids keeping a full Nexus image in
                // memory for every card on the page.
                thumbnail.DecodePixelWidth =
                    DecodePixelWidth;

                thumbnail.UriSource =
                    imageUri;

                thumbnail.EndInit();

                return thumbnail;
            }
            catch
            {
                return DependencyProperty.UnsetValue;
            }
        }

        public object ConvertBack(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
