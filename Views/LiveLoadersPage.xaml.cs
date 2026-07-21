using Limelight.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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
        private bool _shuffleEnabled;
        private Point _dragStartPoint;
        private X19ModChoice? _draggedChoice;

        public event Action<IReadOnlyList<string>>? X19GroupChanged;
        public event Action<bool>? X19ShuffleChanged;
        public event Action? OpenHotkeySettingsRequested;

        public LiveLoadersPage()
        {
            InitializeComponent();
        }

        public void ShowConfiguration(
            IEnumerable<InstalledMod> mods,
            IEnumerable<string>? selectedModIds,
            string activeModId,
            string hotkeyGesture,
            bool shuffleEnabled)
        {
            // I rebuild this small view whenever the library changes so removed
            // mods cannot remain inside the user's X19 rotation.
            HashSet<string> selectedIds =
                new HashSet<string>(
                    selectedModIds ??
                    Enumerable.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase);

            Dictionary<string, int> selectedOrder =
                (selectedModIds ?? Enumerable.Empty<string>())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select((id, index) => new { id, index })
                    .ToDictionary(
                        item => item.id,
                        item => item.index,
                        StringComparer.OrdinalIgnoreCase);

            _isRefreshing = true;
            _shuffleEnabled = shuffleEnabled;

            _modChoices.Clear();

            _modChoices.AddRange(
                mods
                    .OrderBy(mod =>
                        selectedIds.Contains(mod.Id)
                            ? 0
                            : 1)
                    .ThenBy(mod =>
                        selectedOrder.TryGetValue(
                            mod.Id,
                            out int order)
                                ? order
                                : int.MaxValue)
                    .ThenBy(mod => mod.DisplayName)
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

            RefreshRotationModeAppearance();
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

            NormaliseModChoiceOrder();
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

            NormaliseModChoiceOrder();
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

            NormaliseModChoiceOrder();
            SaveGroupSelection();
        }

        private void RotationMode_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not Button button)
            {
                return;
            }

            bool shuffleEnabled =
                string.Equals(
                    button.Tag?.ToString(),
                    "SHUFFLE",
                    StringComparison.OrdinalIgnoreCase);

            if (_shuffleEnabled == shuffleEnabled)
            {
                return;
            }

            _shuffleEnabled = shuffleEnabled;
            RefreshRotationModeAppearance();
            RefreshGroupSummary();
            X19ShuffleChanged?.Invoke(_shuffleEnabled);
        }

        private void ModCard_MouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            _dragStartPoint =
                e.GetPosition(this);

            _draggedChoice =
                (sender as FrameworkElement)?.DataContext as X19ModChoice;

            if (_draggedChoice?.IsSelected != true)
            {
                _draggedChoice = null;
            }
        }

        private void ModCard_MouseMove(
            object sender,
            MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed ||
                _draggedChoice is null)
            {
                return;
            }

            Point currentPosition =
                e.GetPosition(this);

            if (Math.Abs(currentPosition.X - _dragStartPoint.X) <
                    SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(currentPosition.Y - _dragStartPoint.Y) <
                    SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            X19ModChoice draggedChoice =
                _draggedChoice;

            _draggedChoice = null;

            DragDrop.DoDragDrop(
                (DependencyObject)sender,
                draggedChoice,
                DragDropEffects.Move);
        }

        private void ModCard_DragOver(
            object sender,
            DragEventArgs e)
        {
            X19ModChoice? sourceChoice =
                e.Data.GetData(typeof(X19ModChoice)) as X19ModChoice;

            X19ModChoice? targetChoice =
                (sender as FrameworkElement)?.DataContext as X19ModChoice;

            e.Effects =
                sourceChoice?.IsSelected == true &&
                targetChoice?.IsSelected == true &&
                !ReferenceEquals(sourceChoice, targetChoice)
                    ? DragDropEffects.Move
                    : DragDropEffects.None;

            e.Handled = true;
        }

        private void ModCard_Drop(
            object sender,
            DragEventArgs e)
        {
            X19ModChoice? sourceChoice =
                e.Data.GetData(typeof(X19ModChoice)) as X19ModChoice;

            FrameworkElement? targetElement =
                sender as FrameworkElement;

            X19ModChoice? targetChoice =
                targetElement?.DataContext as X19ModChoice;

            if (sourceChoice?.IsSelected != true ||
                targetChoice?.IsSelected != true ||
                ReferenceEquals(sourceChoice, targetChoice) ||
                targetElement is null)
            {
                return;
            }

            bool insertAfter =
                e.GetPosition(targetElement).Y >
                targetElement.ActualHeight / 2;

            _modChoices.Remove(sourceChoice);

            int targetIndex =
                _modChoices.IndexOf(targetChoice);

            if (insertAfter)
            {
                targetIndex++;
            }

            _modChoices.Insert(
                Math.Clamp(
                    targetIndex,
                    0,
                    _modChoices.Count),
                sourceChoice);

            X19ModsList.Items.Refresh();
            SaveGroupSelection();
            e.Handled = true;
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

        private void NormaliseModChoiceOrder()
        {
            List<X19ModChoice> selectedChoices =
                _modChoices
                    .Where(choice => choice.IsSelected)
                    .ToList();

            List<X19ModChoice> unselectedChoices =
                _modChoices
                    .Where(choice => !choice.IsSelected)
                    .OrderBy(choice => choice.DisplayName)
                    .ToList();

            _modChoices.Clear();
            _modChoices.AddRange(selectedChoices);
            _modChoices.AddRange(unselectedChoices);
            X19ModsList.Items.Refresh();
        }

        private void RefreshRotationModeAppearance()
        {
            Brush selectedBackground =
                (Brush)FindResource("PinkBrush");

            Brush normalBackground =
                new SolidColorBrush(
                    Color.FromRgb(37, 41, 67));

            SequentialModeButton.Background =
                _shuffleEnabled
                    ? normalBackground
                    : selectedBackground;

            ShuffleModeButton.Background =
                _shuffleEnabled
                    ? selectedBackground
                    : normalBackground;

            SequentialModeButton.Foreground =
                (Brush)FindResource("TextBrush");

            ShuffleModeButton.Foreground =
                (Brush)FindResource("TextBrush");
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
                    ? _shuffleEnabled
                        ? $"Press {HotkeyText.Text} during gameplay to choose a different selected character at random."
                        : $"Press {HotkeyText.Text} during gameplay to follow the selected order."
                    : "Choose at least one mod before launching with X19 LLoader.";

            GroupStatusTitleText.Foreground =
                (System.Windows.Media.Brush)FindResource(
                    hasSelection
                        ? "CyanBrush"
                        : "PinkBrush");
        }
    }
}
