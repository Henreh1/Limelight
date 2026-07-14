using Limelight.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Limelight.Views
{
    public partial class MyModsPage : UserControl
    {
        public event Action<string>? ToggleModRequested;
        public event Action<string>? RemoveModRequested;

        public MyModsPage()
        {
            InitializeComponent();
        }

        public void ShowMods(
            IEnumerable<InstalledMod> mods,
            string activeModId)
        {
            // Materialise the list once so the count and visible cards
            // always represent the same library snapshot.
            List<InstalledMod> visibleMods =
                mods.ToList();

            foreach (InstalledMod mod in visibleMods)
            {
                mod.IsActive =
                    string.Equals(
                        mod.Id,
                        activeModId,
                        StringComparison.OrdinalIgnoreCase);
            }

            // Resetting the source ensures the active-state button
            // immediately changes between Activate and Deactivate.
            ModsList.ItemsSource = null;
            ModsList.ItemsSource = visibleMods;

            ModCountText.Text =
                visibleMods.Count == 1
                    ? "1 MOD"
                    : $"{visibleMods.Count} MODS";
            ModCountText.Foreground =
    (Brush)FindResource(
        visibleMods.Count == 0
            ? "PinkBrush"
            : "CyanBrush");

            EmptyLibraryText.Visibility =
                visibleMods.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private void ToggleMod_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.Tag is string modId)
            {
                ToggleModRequested?.Invoke(modId);
            }
        }

        private void RemoveMod_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.Tag is string modId)
            {
                RemoveModRequested?.Invoke(modId);
            }
        }
    }
}