using Limelight.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Limelight.Views
{
    public sealed class X19ModChoice
    {
        public string Id { get; init; } =
            string.Empty;

        public string DisplayName { get; init; } =
            "Unnamed mod";

        public bool IsSelected { get; set; }

        public bool IsActive { get; init; }
    }

    public partial class LiveLoadersPage : UserControl
    {
        private readonly List<X19ModChoice> _modChoices =
            new();

        private bool _isRefreshing;

        public event Action<IReadOnlyList<string>>? X19GroupChanged;
        public event Action? OpenHotkeySettingsRequested;

        public LiveLoadersPage()
        {
            InitializeComponent();
        }

        public void ShowConfiguration(
            IEnumerable<InstalledMod> mods,
            IEnumerable<string>? selectedModIds,
            string activeModId,
            string hotkeyGesture)
        {
            // I rebuild this small view whenever the library changes so removed
            // mods cannot remain inside the user's X19 rotation.
            HashSet<string> selectedIds =
                new HashSet<string>(
                    selectedModIds ??
                    Enumerable.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase);

            _isRefreshing = true;

            _modChoices.Clear();

            _modChoices.AddRange(
                mods
                    .OrderBy(mod => mod.DisplayName)
                    .Select(mod =>
                        new X19ModChoice
                        {
                            Id = mod.Id,
                            DisplayName = mod.DisplayName,
                            IsSelected = selectedIds.Contains(mod.Id),
                            IsActive =
                                string.Equals(
                                    mod.Id,
                                    activeModId,
                                    StringComparison.OrdinalIgnoreCase)
                        }));

            X19ModsList.ItemsSource = null;
            X19ModsList.ItemsSource = _modChoices;

            HotkeyText.Text =
                string.IsNullOrWhiteSpace(hotkeyGesture)
                    ? "NOT SET"
                    : hotkeyGesture.ToUpperInvariant();

            InstalledModsEmptyText.Visibility =
                _modChoices.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            X19ModsList.Visibility =
                _modChoices.Count == 0
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            _isRefreshing = false;

            RefreshGroupSummary();
        }

        private void ModSelection_Changed(
            object sender,
            RoutedEventArgs e)
        {
            if (_isRefreshing)
            {
                return;
            }

            if (sender is CheckBox checkBox &&
                checkBox.DataContext is X19ModChoice choice)
            {
                choice.IsSelected =
                    checkBox.IsChecked == true;
            }

            SaveGroupSelection();
        }

        private void SelectAll_Click(
            object sender,
            RoutedEventArgs e)
        {
            foreach (X19ModChoice choice in _modChoices)
            {
                choice.IsSelected = true;
            }

            X19ModsList.Items.Refresh();
            SaveGroupSelection();
        }

        private void ClearGroup_Click(
            object sender,
            RoutedEventArgs e)
        {
            foreach (X19ModChoice choice in _modChoices)
            {
                choice.IsSelected = false;
            }

            X19ModsList.Items.Refresh();
            SaveGroupSelection();
        }

        private void OpenHotkeySettings_Click(
            object sender,
            RoutedEventArgs e)
        {
            OpenHotkeySettingsRequested?.Invoke();
        }

        private void SaveGroupSelection()
        {
            RefreshGroupSummary();

            IReadOnlyList<string> selectedIds =
                _modChoices
                    .Where(choice => choice.IsSelected)
                    .Select(choice => choice.Id)
                    .ToList();

            X19GroupChanged?.Invoke(selectedIds);
        }

        private void RefreshGroupSummary()
        {
            int selectedCount =
                _modChoices.Count(choice => choice.IsSelected);

            SelectedCountText.Text =
                selectedCount == 1
                    ? "1 MOD SELECTED"
                    : $"{selectedCount} MODS SELECTED";

            bool hasSelection =
                selectedCount > 0;

            GroupStatusTitleText.Text =
                hasSelection
                    ? "X19 GROUP READY"
                    : "GROUP NEEDS MODS";

            GroupStatusDetailText.Text =
                hasSelection
                    ? $"Press {HotkeyText.Text} during gameplay to cycle through the selected characters."
                    : "Choose at least one mod before launching with X19 LLoader.";

            GroupStatusTitleText.Foreground =
                (System.Windows.Media.Brush)FindResource(
                    hasSelection
                        ? "CyanBrush"
                        : "PinkBrush");
        }
    }
}