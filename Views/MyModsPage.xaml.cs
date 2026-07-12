using Limelight.Models;
using System.Windows;
using System.Windows.Controls;

namespace Limelight.Views
{
    public partial class MyModsPage : UserControl
    {
        public MyModsPage()
        {
            InitializeComponent();
        }

        public void ShowMods(IEnumerable<InstalledMod> mods)
        {
            // Materialise the list once so the count and visible items
            // always represent the same library snapshot.
            List<InstalledMod> visibleMods =
                mods.ToList();

            ModsList.ItemsSource = visibleMods;

            ModCountText.Text =
                visibleMods.Count == 1
                    ? "1 MOD"
                    : $"{visibleMods.Count} MODS";

            EmptyLibraryText.Visibility =
                visibleMods.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
    }
}