using Limelight.Models;
using Limelight.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

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

    public sealed class X19ProfileChoice
    {
        public string Id { get; init; } =
            string.Empty;

        public string Name { get; init; } =
            "Unnamed profile";

        public List<string> ModIds { get; init; } =
            new();

        public string CountText { get; init; } =
            "0 AVAILABLE";

        public bool IsSelected { get; set; }
    }

    public partial class LiveLoadersPage : UserControl
    {
        private readonly List<X19ModChoice> _modChoices =
            new();

        private readonly List<X19ProfileChoice> _profileChoices =
            new();

        private List<InstalledMod> _availableMods =
            new();

        private string _activeModId =
            string.Empty;

        private bool _isRefreshing;
        private bool _shuffleEnabled;
        private readonly DispatcherTimer _controllerCaptureTimer;
        private bool _isCapturingX19Hotkey;
        private XInputButton _previousControllerButtons;
        private string _savedHotkeyGesture =
            "F8";
        private Point _dragStartPoint;
        private X19ModChoice? _draggedChoice;

        public event Action<IReadOnlyList<string>>? X19GroupChanged;
        public event Action<IReadOnlyList<string>>? X19ProfileGroupsChanged;
        public event Action<bool>? X19ShuffleChanged;
        public event Action<string>? X19HotkeyChanged;

        public LiveLoadersPage()
        {
            InitializeComponent();

            _controllerCaptureTimer =
                new DispatcherTimer
                {
                    Interval =
                        TimeSpan.FromMilliseconds(
                            35)
                };

            _controllerCaptureTimer.Tick +=
                ControllerCaptureTimer_Tick;

            Unloaded +=
                (_, _) =>
                {
                    _controllerCaptureTimer.Stop();
                    _isCapturingX19Hotkey = false;
                };
        }

        public void ShowConfiguration(
            IEnumerable<InstalledMod> mods,
            IEnumerable<string>? selectedModIds,
            IEnumerable<string>? selectedProfileIds,
            string activeModId,
            string hotkeyGesture,
            bool shuffleEnabled,
            IEnumerable<ModProfile>? profiles = null)
        {
            // I rebuild this small view whenever the library changes so removed
            // mods cannot remain inside the user's X19 rotation.
            HashSet<string> selectedIds =
                new(
                    selectedModIds ?? Enumerable.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase);

            HashSet<string> selectedProfiles =
                new(
                    selectedProfileIds ?? Enumerable.Empty<string>(),
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

            _availableMods =
                mods
                    .OrderBy(mod => mod.DisplayName)
                    .ToList();

            _activeModId =
                activeModId;

            HashSet<string> availableModIds =
                _availableMods
                    .Select(mod => mod.Id)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _profileChoices.Clear();

            foreach (ModProfile profile in
                     profiles ?? Enumerable.Empty<ModProfile>())
            {
                List<string> profileModIds =
                    (profile.ModIds ?? new List<string>())
                        .Where(availableModIds.Contains)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                _profileChoices.Add(
                    new X19ProfileChoice
                    {
                        Id = profile.Id,
                        Name = profile.Name,
                        ModIds = profileModIds,
                        IsSelected =
                            selectedProfiles.Contains(profile.Id) &&
                            profileModIds.Count > 0,
                        CountText =
                            profileModIds.Count == 1
                                ? "1 AVAILABLE"
                                : $"{profileModIds.Count} AVAILABLE"
                    });
            }

            HashSet<string> groupedModIds =
                GetSelectedProfileModIds();

            _modChoices.Clear();

            _modChoices.AddRange(
                _availableMods
                    .Where(mod => !groupedModIds.Contains(mod.Id))
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

            X19ProfilesList.ItemsSource = null;
            X19ProfilesList.ItemsSource = _profileChoices;

            ProfilesEmptyText.Visibility =
                _profileChoices.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            X19ProfilesList.Visibility =
                _profileChoices.Count == 0
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            _savedHotkeyGesture =
                string.IsNullOrWhiteSpace(hotkeyGesture)
                    ? "F8"
                    : hotkeyGesture;

            if (!_isCapturingX19Hotkey)
            {
                HotkeyText.Text =
                    _savedHotkeyGesture.ToUpperInvariant();
            }

            InstalledModsEmptyText.Visibility =
                _modChoices.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            X19ModsList.Visibility =
                _modChoices.Count == 0
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            InstalledModsEmptyText.Text =
                _availableMods.Count == 0
                    ? "Import a mod before building an X19 rotation."
                    : "Every available mod is already supplied by a selected profile group.";

            _isRefreshing = false;

            RefreshRotationModeAppearance();
            RefreshGroupSummary();
        }

        private void AddProfileToRotation_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not Button button ||
                button.Tag is not string profileId)
            {
                return;
            }

            X19ProfileChoice? profile =
                _profileChoices.FirstOrDefault(choice =>
                    string.Equals(
                        choice.Id,
                        profileId,
                        StringComparison.OrdinalIgnoreCase));

            if (profile is null ||
                profile.ModIds.Count == 0)
            {
                GroupStatusTitleText.Text =
                    "PROFILE HAS NO AVAILABLE MODS";

                GroupStatusDetailText.Text =
                    "Import or restore one of this profile's mods, then try adding it again.";

                GroupStatusTitleText.Foreground =
                    (Brush)FindResource("PinkBrush");

                return;
            }

            List<string> selectedIndividualIds =
                _modChoices
                    .Where(choice => choice.IsSelected)
                    .Select(choice => choice.Id)
                    .ToList();

            profile.IsSelected =
                !profile.IsSelected;

            // A profile owns its cast while selected. I remove those same
            // entries from the individual picker so the rotation has one
            // clear source for every character.
            RebuildIndividualChoices(selectedIndividualIds);
            X19ProfilesList.Items.Refresh();
            SaveGroupSelection();

            GroupStatusTitleText.Text =
                profile.IsSelected
                    ? "PROFILE GROUP SELECTED"
                    : "PROFILE GROUP REMOVED";

            GroupStatusDetailText.Text =
                profile.IsSelected
                    ? $"{profile.Name} now supplies its cast to X19. Those characters were removed from the individual selector."
                    : $"{profile.Name} was removed from X19. Its characters are available in the individual selector again.";

            GroupStatusTitleText.Foreground =
                (Brush)FindResource("CyanBrush");
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
            foreach (X19ProfileChoice profile in _profileChoices)
            {
                profile.IsSelected = false;
            }

            RebuildIndividualChoices();

            foreach (X19ModChoice choice in _modChoices)
            {
                choice.IsSelected = false;
            }

            X19ProfilesList.Items.Refresh();
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

        private void CaptureX19Hotkey_Click(
            object sender,
            RoutedEventArgs e)
        {
            _isCapturingX19Hotkey = true;

            XInputControllerService.TryReadCombinedButtons(
                out _previousControllerButtons);

            _controllerCaptureTimer.Start();

            CaptureX19HotkeyButton.Content =
                "PRESS INPUT";

            GroupStatusTitleText.Text =
                "LISTENING FOR INPUT";

            GroupStatusDetailText.Text =
                "Press a keyboard combination or controller button now. Press Escape to keep the current binding.";

            GroupStatusTitleText.Foreground =
                (Brush)FindResource("PinkBrush");

            CaptureX19HotkeyButton.Focus();
            Keyboard.Focus(
                CaptureX19HotkeyButton);
        }

        private void CaptureX19Hotkey_PreviewKeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (!_isCapturingX19Hotkey)
            {
                return;
            }

            e.Handled = true;

            Key pressedKey =
                e.Key == Key.System
                    ? e.SystemKey
                    : e.Key;

            if (pressedKey == Key.Escape)
            {
                FinishHotkeyCapture(
                    _savedHotkeyGesture,
                    saveChange: false);

                return;
            }

            if (IsModifierKey(pressedKey) ||
                pressedKey == Key.None)
            {
                GroupStatusDetailText.Text =
                    "Add a letter, number, or function key to the combination.";

                return;
            }

            ModifierKeys modifiers =
                Keyboard.Modifiers &
                (ModifierKeys.Control |
                 ModifierKeys.Alt |
                 ModifierKeys.Shift);

            FinishHotkeyCapture(
                CreateGestureText(
                    pressedKey,
                    modifiers),
                saveChange: true);
        }

        private void FinishHotkeyCapture(
            string gesture,
            bool saveChange)
        {
            _isCapturingX19Hotkey = false;
            _controllerCaptureTimer.Stop();

            CaptureX19HotkeyButton.Content =
                "CHANGE HOTKEY";

            if (saveChange)
            {
                _savedHotkeyGesture =
                    gesture;

                HotkeyText.Text =
                    gesture.ToUpperInvariant();

                X19HotkeyChanged?.Invoke(
                    gesture);
            }

            RefreshGroupSummary();
        }

        private void ControllerCaptureTimer_Tick(
            object? sender,
            EventArgs e)
        {
            if (!_isCapturingX19Hotkey ||
                !XInputControllerService.TryReadCombinedButtons(
                    out XInputButton currentButtons))
            {
                return;
            }

            XInputButton newlyPressedButtons =
                currentButtons &
                ~_previousControllerButtons;

            _previousControllerButtons =
                currentButtons;

            if (!XInputControllerService.TryCreateGesture(
                    newlyPressedButtons,
                    out string gesture))
            {
                return;
            }

            // I capture the button edge so a held controller input cannot
            // register repeatedly while the assignment card is listening.
            FinishHotkeyCapture(
                gesture,
                saveChange: true);
        }

        private static bool IsModifierKey(
            Key key)
        {
            return key is
                Key.LeftCtrl or
                Key.RightCtrl or
                Key.LeftAlt or
                Key.RightAlt or
                Key.LeftShift or
                Key.RightShift or
                Key.LWin or
                Key.RWin;
        }

        private static string CreateGestureText(
            Key key,
            ModifierKeys modifiers)
        {
            List<string> parts =
                new();

            if (modifiers.HasFlag(
                    ModifierKeys.Control))
            {
                parts.Add("CTRL");
            }

            if (modifiers.HasFlag(
                    ModifierKeys.Alt))
            {
                parts.Add("ALT");
            }

            if (modifiers.HasFlag(
                    ModifierKeys.Shift))
            {
                parts.Add("SHIFT");
            }

            parts.Add(
                key switch
                {
                    Key.D0 => "0",
                    Key.D1 => "1",
                    Key.D2 => "2",
                    Key.D3 => "3",
                    Key.D4 => "4",
                    Key.D5 => "5",
                    Key.D6 => "6",
                    Key.D7 => "7",
                    Key.D8 => "8",
                    Key.D9 => "9",
                    _ => key.ToString().ToUpperInvariant()
                });

            return string.Join(
                "+",
                parts);
        }

        private void SaveGroupSelection()
        {
            RefreshGroupSummary();

            IReadOnlyList<string> selectedIds =
                BuildSelectedRotationIds();

            IReadOnlyList<string> selectedProfileIds =
                _profileChoices
                    .Where(profile => profile.IsSelected)
                    .Select(profile => profile.Id)
                    .ToList();

            X19GroupChanged?.Invoke(selectedIds);
            X19ProfileGroupsChanged?.Invoke(selectedProfileIds);
        }

        private IReadOnlyList<string> BuildSelectedRotationIds()
        {
            return _profileChoices
                .Where(profile => profile.IsSelected)
                .SelectMany(profile => profile.ModIds)
                .Concat(
                    _modChoices
                        .Where(choice => choice.IsSelected)
                        .Select(choice => choice.Id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private HashSet<string> GetSelectedProfileModIds()
        {
            return _profileChoices
                .Where(profile => profile.IsSelected)
                .SelectMany(profile => profile.ModIds)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private void RebuildIndividualChoices(
            IEnumerable<string>? selectedIndividualIds = null)
        {
            HashSet<string> selectedIds =
                new(
                    selectedIndividualIds ??
                    _modChoices
                        .Where(choice => choice.IsSelected)
                        .Select(choice => choice.Id),
                    StringComparer.OrdinalIgnoreCase);

            Dictionary<string, int> selectedOrder =
                _modChoices
                    .Where(choice => choice.IsSelected)
                    .Select((choice, index) => new { choice.Id, index })
                    .ToDictionary(
                        item => item.Id,
                        item => item.index,
                        StringComparer.OrdinalIgnoreCase);

            HashSet<string> groupedModIds =
                GetSelectedProfileModIds();

            _modChoices.Clear();
            _modChoices.AddRange(
                _availableMods
                    .Where(mod => !groupedModIds.Contains(mod.Id))
                    .OrderBy(mod => selectedIds.Contains(mod.Id) ? 0 : 1)
                    .ThenBy(mod =>
                        selectedOrder.TryGetValue(mod.Id, out int order)
                            ? order
                            : int.MaxValue)
                    .ThenBy(mod => mod.DisplayName)
                    .Select(mod => new X19ModChoice
                    {
                        Id = mod.Id,
                        DisplayName = mod.DisplayName,
                        IsSelected = selectedIds.Contains(mod.Id),
                        IsActive =
                            string.Equals(
                                mod.Id,
                                _activeModId,
                                StringComparison.OrdinalIgnoreCase)
                    }));

            X19ModsList.ItemsSource = null;
            X19ModsList.ItemsSource = _modChoices;
            InstalledModsEmptyText.Visibility =
                _modChoices.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            X19ModsList.Visibility =
                _modChoices.Count == 0
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            InstalledModsEmptyText.Text =
                _availableMods.Count == 0
                    ? "Import a mod before building an X19 rotation."
                    : "Every available mod is already supplied by a selected profile group.";
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
                BuildSelectedRotationIds().Count;

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
