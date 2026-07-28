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
    public sealed class ProfileModChoice
    {
        public string Id { get; init; } = string.Empty;
        public string DisplayName { get; init; } = "Unnamed mod";
        public bool IsSelected { get; set; }
    }

    public sealed class ModProfileEditorChoice
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; set; } = "New profile";
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; set; }
        public List<ProfileModChoice> Mods { get; init; } = new();

        public string Monogram
        {
            get
            {
                string[] words =
                    Name
                        .Split(
                            ' ',
                            StringSplitOptions.RemoveEmptyEntries |
                            StringSplitOptions.TrimEntries);

                if (words.Length == 0)
                {
                    return "P";
                }

                if (words.Length == 1)
                {
                    return words[0]
                        .Substring(0, Math.Min(3, words[0].Length))
                        .ToUpperInvariant();
                }

                // Initials keep longer profile names readable inside the
                // small file badge without turning it into a word cloud.
                return string.Concat(
                        words
                            .Take(3)
                            .Select(word => word[0]))
                    .ToUpperInvariant();
            }
        }

        public double MonogramFontSize =>
            Monogram.Length switch
            {
                1 => 18,
                2 => 15,
                _ => 12
            };

        public string CountText =>
            Mods.Count(mod => mod.IsSelected) == 1
                ? "1 CHARACTER"
                : $"{Mods.Count(mod => mod.IsSelected)} CHARACTERS";
    }

    public partial class ProfilesPage : UserControl
    {
        private readonly List<ModProfileEditorChoice> _profileChoices = new();
        private List<InstalledMod> _availableMods = new();
        private readonly HashSet<string> _editingOriginalModIds =
            new(StringComparer.OrdinalIgnoreCase);
        private string _editingProfileId = string.Empty;
        private bool _isRefreshing;

        public event Action<IReadOnlyList<ModProfile>>? ProfilesChanged;
        public event Action<string>? UseProfileInX19Requested;

        public ProfilesPage()
        {
            InitializeComponent();
        }

        public void ShowProfiles(
            IEnumerable<ModProfile>? profiles,
            IEnumerable<InstalledMod> mods)
        {
            bool keepEditorOpen =
                ProfileEditorPanel.Visibility == Visibility.Visible;
            string openProfileId = _editingProfileId;

            _isRefreshing = true;
            _availableMods = mods.OrderBy(mod => mod.DisplayName).ToList();
            _profileChoices.Clear();

            foreach (ModProfile profile in profiles ?? Enumerable.Empty<ModProfile>())
            {
                HashSet<string> selectedIds =
                    new(profile.ModIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

                _profileChoices.Add(
                    CreateEditorChoice(
                        profile.Id,
                        profile.Name,
                        profile.CreatedAt,
                        profile.UpdatedAt,
                        selectedIds));
            }

            RefreshProfileList();

            if (keepEditorOpen && FindProfile(openProfileId) is not null)
            {
                OpenProfile(openProfileId);
            }
            else
            {
                ShowBrowser();
            }

            _isRefreshing = false;
        }

        private ModProfileEditorChoice CreateEditorChoice(
            string id,
            string name,
            DateTimeOffset createdAt,
            DateTimeOffset updatedAt,
            IReadOnlySet<string> selectedIds)
        {
            DateTimeOffset safeCreatedAt =
                createdAt == default ? DateTimeOffset.Now : createdAt;

            return new ModProfileEditorChoice
            {
                Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
                Name = string.IsNullOrWhiteSpace(name) ? "New profile" : name.Trim(),
                CreatedAt = safeCreatedAt,
                UpdatedAt = updatedAt == default ? safeCreatedAt : updatedAt,
                Mods = _availableMods
                    .Select(mod => new ProfileModChoice
                    {
                        Id = mod.Id,
                        DisplayName = mod.DisplayName,
                        IsSelected = selectedIds.Contains(mod.Id)
                    })
                    .ToList()
            };
        }

        private void CreateProfile_Click(object sender, RoutedEventArgs e)
        {
            string profileName = NewProfileNameBox.Text.Trim();

            if (profileName.Length == 0)
            {
                SetBrowserStatus("Give the new profile a name before creating it.", true);
                NewProfileNameBox.Focus();
                return;
            }

            if (_profileChoices.Any(profile =>
                    string.Equals(profile.Name.Trim(), profileName, StringComparison.OrdinalIgnoreCase)))
            {
                SetBrowserStatus("A profile with that name already exists.", true);
                return;
            }

            DateTimeOffset now = DateTimeOffset.Now;

            _profileChoices.Add(
                CreateEditorChoice(
                    Guid.NewGuid().ToString("N"),
                    profileName,
                    now,
                    now,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)));

            NewProfileNameBox.Clear();
            RefreshProfileList();
            PublishProfiles();
            SetBrowserStatus($"{profileName} was created. Open its card to choose the cast.", false);
        }

        private void NewProfileNameBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            CreateProfile_Click(sender, e);
        }

        private void ProfileCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2 ||
                sender is not FrameworkElement card ||
                card.DataContext is not ModProfileEditorChoice profile)
            {
                return;
            }

            e.Handled = true;
            OpenProfile(profile.Id);
        }

        private void OpenProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string profileId)
            {
                OpenProfile(profileId);
            }
        }

        private void OpenProfile(string profileId)
        {
            ModProfileEditorChoice? profile = FindProfile(profileId);

            if (profile is null)
            {
                return;
            }

            _editingProfileId = profile.Id;
            _editingOriginalModIds.Clear();

            foreach (ProfileModChoice mod in profile.Mods.Where(mod => mod.IsSelected))
            {
                _editingOriginalModIds.Add(mod.Id);
            }

            _isRefreshing = true;
            EditorHeadingText.Text = profile.Name.ToUpperInvariant();
            EditorNameBox.Text = profile.Name;
            EditorModsList.ItemsSource = null;
            EditorModsList.ItemsSource = profile.Mods;
            UpdateEditorCount(profile);
            EditorEmptyModsText.Visibility =
                profile.Mods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EditorModsList.Visibility =
                profile.Mods.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            EditorStatusText.Text = "Changes are saved only when you choose Save Changes.";
            EditorStatusText.Foreground = (Brush)FindResource("MutedTextBrush");
            ProfilesBrowserPanel.Visibility = Visibility.Collapsed;
            ProfileEditorPanel.Visibility = Visibility.Visible;
            _isRefreshing = false;
        }

        private void BackToProfiles_Click(object sender, RoutedEventArgs e)
        {
            ModProfileEditorChoice? profile = FindProfile(_editingProfileId);

            if (profile is not null)
            {
                // I restore the saved cast here so Back never quietly saves
                // a half-finished edit to the user's profile.
                foreach (ProfileModChoice mod in profile.Mods)
                {
                    mod.IsSelected = _editingOriginalModIds.Contains(mod.Id);
                }
            }

            ShowBrowser();
            RefreshProfileList();
        }

        private void EditorModSelection_Changed(object sender, RoutedEventArgs e)
        {
            if (_isRefreshing)
            {
                return;
            }

            ModProfileEditorChoice? profile = FindProfile(_editingProfileId);

            if (profile is null)
            {
                return;
            }

            UpdateEditorCount(profile);
            SetEditorStatus("Profile changes are waiting to be saved.", true);
        }

        private void SaveEditorProfile_Click(object sender, RoutedEventArgs e)
        {
            SaveEditingProfile();
        }

        private void SetEditorAsX19_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveEditingProfile())
            {
                return;
            }

            ModProfileEditorChoice? profile = FindProfile(_editingProfileId);

            if (profile is null)
            {
                return;
            }

            if (!profile.Mods.Any(mod => mod.IsSelected))
            {
                LimelightDialog.Open(
                    Window.GetWindow(this),
                    "PROFILE NEEDS CHARACTERS",
                    "Choose at least one mod before setting this profile as the X19 rotation.",
                    LimelightDialogTone.Warning,
                    eyebrow: "EMPTY CAST");
                return;
            }

            UseProfileInX19Requested?.Invoke(profile.Id);
        }

        private void DeleteEditorProfile_Click(object sender, RoutedEventArgs e)
        {
            ModProfileEditorChoice? profile = FindProfile(_editingProfileId);

            if (profile is null)
            {
                return;
            }

            LimelightDialogChoice choice = LimelightDialog.Open(
                Window.GetWindow(this),
                "DELETE PROFILE?",
                $"{profile.Name} will be removed. Your installed mods and current X19 rotation will stay untouched.",
                LimelightDialogTone.Question,
                primaryAction: "DELETE PROFILE",
                secondaryAction: "KEEP PROFILE",
                eyebrow: "PROFILE LIBRARY");

            if (choice != LimelightDialogChoice.Primary)
            {
                return;
            }

            string removedName = profile.Name;
            _profileChoices.Remove(profile);
            PublishProfiles();
            ShowBrowser();
            RefreshProfileList();
            SetBrowserStatus($"{removedName} was removed.", false);
        }

        private bool SaveEditingProfile()
        {
            ModProfileEditorChoice? profile = FindProfile(_editingProfileId);

            if (profile is null)
            {
                return false;
            }

            string profileName = EditorNameBox.Text.Trim();

            if (profileName.Length == 0)
            {
                SetEditorStatus("Every profile needs a name before it can be saved.", true);
                EditorNameBox.Focus();
                return false;
            }

            if (_profileChoices.Any(other =>
                    !string.Equals(other.Id, profile.Id, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(other.Name.Trim(), profileName, StringComparison.OrdinalIgnoreCase)))
            {
                SetEditorStatus("Profile names must be unique.", true);
                return false;
            }

            profile.Name = profileName;
            profile.UpdatedAt = DateTimeOffset.Now;
            _editingOriginalModIds.Clear();

            foreach (ProfileModChoice mod in profile.Mods.Where(mod => mod.IsSelected))
            {
                _editingOriginalModIds.Add(mod.Id);
            }

            EditorHeadingText.Text = profile.Name.ToUpperInvariant();
            UpdateEditorCount(profile);
            RefreshProfileList();
            PublishProfiles();
            SetEditorStatus($"{profile.Name} has been saved.", false);
            return true;
        }

        private void SetBrowserStatus(string message, bool isError)
        {
            ProfileStatusText.Text = message;
            ProfileStatusText.Foreground =
                (Brush)FindResource(isError ? "PinkBrush" : "CyanBrush");
        }

        private void SetEditorStatus(string message, bool isError)
        {
            EditorStatusText.Text = message;
            EditorStatusText.Foreground =
                (Brush)FindResource(isError ? "PinkBrush" : "CyanBrush");
        }

        private void UpdateEditorCount(ModProfileEditorChoice profile)
        {
            EditorCountText.Text = profile.CountText;
            EditorCountText.Foreground =
                (Brush)FindResource(
                    profile.Mods.Any(mod => mod.IsSelected) ? "CyanBrush" : "PinkBrush");
        }

        private void ShowBrowser()
        {
            _editingProfileId = string.Empty;
            _editingOriginalModIds.Clear();
            EditorModsList.ItemsSource = null;
            ProfileEditorPanel.Visibility = Visibility.Collapsed;
            ProfilesBrowserPanel.Visibility = Visibility.Visible;
        }

        private ModProfileEditorChoice? FindProfile(string profileId)
        {
            return _profileChoices.FirstOrDefault(profile =>
                string.Equals(profile.Id, profileId, StringComparison.OrdinalIgnoreCase));
        }

        private void PublishProfiles()
        {
            IReadOnlyList<ModProfile> profiles = _profileChoices
                .Select(profile => new ModProfile
                {
                    Id = profile.Id,
                    Name = profile.Name.Trim(),
                    CreatedAt = profile.CreatedAt,
                    UpdatedAt = profile.UpdatedAt,
                    ModIds = profile.Mods
                        .Where(mod => mod.IsSelected)
                        .Select(mod => mod.Id)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                })
                .ToList();

            ProfilesChanged?.Invoke(profiles);
        }

        private void RefreshProfileList()
        {
            ProfilesList.ItemsSource = null;
            ProfilesList.ItemsSource = _profileChoices;
            ProfileCountText.Text =
                _profileChoices.Count == 1 ? "1 PROFILE" : $"{_profileChoices.Count} PROFILES";
            ProfileCountText.Foreground =
                (Brush)FindResource(_profileChoices.Count == 0 ? "PinkBrush" : "CyanBrush");
            EmptyProfilesPanel.Visibility =
                _profileChoices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ProfilesScrollViewer.Visibility =
                _profileChoices.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
