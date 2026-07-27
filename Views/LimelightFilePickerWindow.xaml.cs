using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Limelight.Views
{
    public enum LimelightPickerMode
    {
        OpenFile,
        OpenFiles,
        SaveFile,
        SelectFolder
    }

    public partial class LimelightFilePickerWindow : Window
    {
        private sealed class PickerEntry
        {
            public required string Name { get; init; }
            public required string FullPath { get; init; }
            public required bool IsDirectory { get; init; }
            public required string TypeLabel { get; init; }
            public required string ModifiedLabel { get; init; }
            public required string SizeLabel { get; init; }
            public string Icon => IsDirectory ? "\u25C6" : "\u25C7";
            public Brush Accent =>
                (Brush)Application.Current.FindResource(
                    IsDirectory ? "CyanBrush" : "PinkBrush");
        }

        private sealed class PickerPlace
        {
            public required string Name { get; init; }
            public required string Path { get; init; }
        }

        private readonly LimelightPickerMode _mode;
        private readonly HashSet<string> _allowedExtensions;
        private readonly string _defaultExtension;
        private readonly ObservableCollection<PickerEntry> _entries = new();
        private readonly ObservableCollection<PickerPlace> _places = new();
        private readonly List<string> _history = new();
        private int _historyIndex = -1;
        private bool _changingPlaceSelection;
        private string _currentDirectory = string.Empty;

        public IReadOnlyList<string> SelectedPaths { get; private set; } =
            Array.Empty<string>();

        public string? SelectedPath => SelectedPaths.FirstOrDefault();

        private LimelightFilePickerWindow(
            LimelightPickerMode mode,
            string title,
            string? initialDirectory,
            IEnumerable<string>? allowedExtensions,
            string filterDescription,
            string? defaultFileName,
            string? defaultExtension)
        {
            InitializeComponent();

            _mode = mode;
            _allowedExtensions =
                new HashSet<string>(
                    (allowedExtensions ?? Array.Empty<string>())
                        .Select(NormalizeExtension)
                        .Where(extension => extension.Length > 0),
                    StringComparer.OrdinalIgnoreCase);

            _defaultExtension =
                NormalizeExtension(defaultExtension ?? string.Empty);

            PickerHeadingText.Text = title.ToUpperInvariant();
            PickerEyebrowText.Text = mode switch
            {
                LimelightPickerMode.SelectFolder => "CHOOSE A FOLDER",
                LimelightPickerMode.SaveFile => "CHOOSE WHERE TO SAVE",
                LimelightPickerMode.OpenFiles => "CHOOSE YOUR EVIDENCE",
                _ => "CHOOSE A FILE"
            };

            FilterBadgeText.Text =
                string.IsNullOrWhiteSpace(filterDescription)
                    ? "ALL FILES"
                    : filterDescription.ToUpperInvariant();

            SelectButton.Content = mode switch
            {
                LimelightPickerMode.SelectFolder => "SELECT FOLDER",
                LimelightPickerMode.SaveFile => "SAVE HERE",
                LimelightPickerMode.OpenFiles => "ADD FILES",
                _ => "SELECT FILE"
            };

            FileNamePanel.Visibility =
                mode == LimelightPickerMode.SelectFolder
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            EntryList.SelectionMode =
                mode == LimelightPickerMode.OpenFiles
                    ? SelectionMode.Extended
                    : SelectionMode.Single;

            FileNameBox.Text = defaultFileName ?? string.Empty;
            EntryList.ItemsSource = _entries;
            PlacesList.ItemsSource = _places;

            WireEvents();
            LoadPlaces();

            string startingDirectory =
                FindStartingDirectory(initialDirectory);

            NavigateTo(startingDirectory, addToHistory: true);
        }

        public static string? PickFolder(
            Window owner,
            string title,
            string? initialDirectory = null)
        {
            var picker =
                Create(
                    owner,
                    LimelightPickerMode.SelectFolder,
                    title,
                    initialDirectory,
                    null,
                    "FOLDERS",
                    null,
                    null);

            picker.ShowDialog();
            return picker.SelectedPath;
        }

        public static string? PickFile(
            Window owner,
            string title,
            string? initialDirectory,
            IEnumerable<string> allowedExtensions,
            string filterDescription)
        {
            var picker =
                Create(
                    owner,
                    LimelightPickerMode.OpenFile,
                    title,
                    initialDirectory,
                    allowedExtensions,
                    filterDescription,
                    null,
                    null);

            picker.ShowDialog();
            return picker.SelectedPath;
        }

        public static IReadOnlyList<string> PickFiles(
            Window owner,
            string title,
            string? initialDirectory,
            IEnumerable<string> allowedExtensions,
            string filterDescription)
        {
            var picker =
                Create(
                    owner,
                    LimelightPickerMode.OpenFiles,
                    title,
                    initialDirectory,
                    allowedExtensions,
                    filterDescription,
                    null,
                    null);

            picker.ShowDialog();
            return picker.SelectedPaths;
        }

        public static string? PickSaveFile(
            Window owner,
            string title,
            string? initialDirectory,
            string defaultFileName,
            string defaultExtension,
            string filterDescription)
        {
            var picker =
                Create(
                    owner,
                    LimelightPickerMode.SaveFile,
                    title,
                    initialDirectory,
                    new[] { defaultExtension },
                    filterDescription,
                    defaultFileName,
                    defaultExtension);

            picker.ShowDialog();
            return picker.SelectedPath;
        }

        private static LimelightFilePickerWindow Create(
            Window owner,
            LimelightPickerMode mode,
            string title,
            string? initialDirectory,
            IEnumerable<string>? allowedExtensions,
            string filterDescription,
            string? defaultFileName,
            string? defaultExtension)
        {
            return new LimelightFilePickerWindow(
                mode,
                title,
                initialDirectory,
                allowedExtensions,
                filterDescription,
                defaultFileName,
                defaultExtension)
            {
                Owner = owner
            };
        }

        private void WireEvents()
        {
            CloseButton.Click += (_, _) => Close();
            CancelButton.Click += (_, _) => Close();
            SelectButton.Click += (_, _) => AcceptSelection();
            BackButton.Click += (_, _) => MoveThroughHistory(-1);
            UpButton.Click += (_, _) => NavigateUp();
            RefreshButton.Click += (_, _) => RefreshCurrentDirectory();
            EntryList.SelectionChanged += EntryList_SelectionChanged;
            EntryList.MouseDoubleClick += EntryList_MouseDoubleClick;
            PlacesList.SelectionChanged += PlacesList_SelectionChanged;
            AddressBox.KeyDown += AddressBox_KeyDown;
            FileNameBox.KeyDown += FileNameBox_KeyDown;
            KeyDown += Window_KeyDown;

            TitleBar.MouseLeftButtonDown += (_, eventArgs) =>
            {
                if (eventArgs.ChangedButton == MouseButton.Left)
                {
                    DragMove();
                }
            };
        }

        private void LoadPlaces()
        {
            _places.Clear();

            AddPlace("HOME", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            AddPlace("DESKTOP", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            AddPlace("DOWNLOADS", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
            AddPlace("DOCUMENTS", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

            foreach (DriveInfo drive in DriveInfo.GetDrives().Where(drive => drive.IsReady))
            {
                AddPlace($"{drive.Name.TrimEnd('\\')} DRIVE", drive.RootDirectory.FullName);
            }
        }

        private void AddPlace(
            string name,
            string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                _places.Add(
                    new PickerPlace
                    {
                        Name = name,
                        Path = path
                    });
            }
        }

        private static string FindStartingDirectory(
            string? requestedPath)
        {
            if (!string.IsNullOrWhiteSpace(requestedPath))
            {
                string candidate =
                    Directory.Exists(requestedPath)
                        ? requestedPath
                        : Path.GetDirectoryName(requestedPath) ?? string.Empty;

                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            string downloads =
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads");

            return Directory.Exists(downloads)
                ? downloads
                : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        private void NavigateTo(
            string path,
            bool addToHistory)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);

                if (!Directory.Exists(fullPath))
                {
                    ShowStatus("THAT FOLDER COULD NOT BE FOUND", isError: true);
                    return;
                }

                List<PickerEntry> directoryEntries =
                    Directory.EnumerateDirectories(fullPath)
                        .Select(CreateDirectoryEntry)
                        .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                List<PickerEntry> fileEntries =
                    _mode == LimelightPickerMode.SelectFolder
                        ? new List<PickerEntry>()
                        : Directory.EnumerateFiles(fullPath)
                            .Where(IsAllowedFile)
                            .Select(CreateFileEntry)
                            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                _entries.Clear();

                foreach (PickerEntry entry in directoryEntries.Concat(fileEntries))
                {
                    _entries.Add(entry);
                }

                _currentDirectory = fullPath;
                AddressBox.Text = fullPath;
                CurrentLocationText.Text =
                    new DirectoryInfo(fullPath).Name.ToUpperInvariant();

                if (string.IsNullOrWhiteSpace(CurrentLocationText.Text))
                {
                    CurrentLocationText.Text = fullPath.ToUpperInvariant();
                }

                EmptyFolderText.Visibility =
                    _entries.Count == 0
                        ? Visibility.Visible
                        : Visibility.Collapsed;

                ShowStatus(
                    _mode == LimelightPickerMode.SelectFolder
                        ? "SELECT THIS FOLDER OR OPEN ANOTHER ONE"
                        : $"{fileEntries.Count} MATCHING FILE(S)",
                    isError: false);

                if (addToHistory)
                {
                    if (_historyIndex < _history.Count - 1)
                    {
                        _history.RemoveRange(
                            _historyIndex + 1,
                            _history.Count - _historyIndex - 1);
                    }

                    _history.Add(fullPath);
                    _historyIndex = _history.Count - 1;
                }

                UpdateNavigationButtons();
                ClearPlaceSelection();
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException ||
                exception is IOException ||
                exception is ArgumentException)
            {
                ShowStatus(
                    $"LIMELIGHT CANNOT OPEN THIS LOCATION: {exception.Message}",
                    isError: true);
            }
        }

        private static PickerEntry CreateDirectoryEntry(
            string path)
        {
            var directory = new DirectoryInfo(path);

            return new PickerEntry
            {
                Name = directory.Name,
                FullPath = directory.FullName,
                IsDirectory = true,
                TypeLabel = "FOLDER",
                ModifiedLabel = SafeModifiedLabel(directory),
                SizeLabel = string.Empty
            };
        }

        private static PickerEntry CreateFileEntry(
            string path)
        {
            var file = new FileInfo(path);

            return new PickerEntry
            {
                Name = file.Name,
                FullPath = file.FullName,
                IsDirectory = false,
                TypeLabel = file.Extension.TrimStart('.').ToUpperInvariant(),
                ModifiedLabel = SafeModifiedLabel(file),
                SizeLabel = FormatFileSize(file.Length)
            };
        }

        private static string SafeModifiedLabel(
            FileSystemInfo item)
        {
            try
            {
                return item.LastWriteTime.ToString("dd MMM yyyy  HH:mm");
            }
            catch
            {
                return string.Empty;
            }
        }

        private bool IsAllowedFile(
            string path)
        {
            return _allowedExtensions.Count == 0 ||
                   _allowedExtensions.Contains(
                       Path.GetExtension(path));
        }

        private static string NormalizeExtension(
            string extension)
        {
            string value = extension.Trim();

            if (value.Length == 0 || value == "." || value == "*.*")
            {
                return string.Empty;
            }

            value = value.Replace("*", string.Empty);
            return value.StartsWith('.') ? value : $".{value}";
        }

        private static string FormatFileSize(
            long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double size = bytes;
            int unitIndex = 0;

            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return unitIndex == 0
                ? $"{size:0} {units[unitIndex]}"
                : $"{size:0.##} {units[unitIndex]}";
        }

        private void EntryList_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            List<PickerEntry> selectedFiles =
                EntryList.SelectedItems
                    .OfType<PickerEntry>()
                    .Where(entry => !entry.IsDirectory)
                    .ToList();

            if (_mode == LimelightPickerMode.OpenFiles)
            {
                FileNameBox.Text =
                    selectedFiles.Count == 0
                        ? string.Empty
                        : $"{selectedFiles.Count} file(s) selected";
            }
            else if (selectedFiles.Count == 1)
            {
                FileNameBox.Text = selectedFiles[0].Name;
            }
        }

        private void EntryList_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
        {
            if (EntryList.SelectedItem is not PickerEntry entry)
            {
                return;
            }

            if (entry.IsDirectory)
            {
                NavigateTo(entry.FullPath, addToHistory: true);
            }
            else if (_mode != LimelightPickerMode.SaveFile)
            {
                AcceptSelection();
            }
        }

        private void PlacesList_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (_changingPlaceSelection ||
                PlacesList.SelectedItem is not PickerPlace place)
            {
                return;
            }

            NavigateTo(place.Path, addToHistory: true);
        }

        private void ClearPlaceSelection()
        {
            _changingPlaceSelection = true;
            PlacesList.SelectedItem = null;
            _changingPlaceSelection = false;
        }

        private void AddressBox_KeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                NavigateTo(AddressBox.Text.Trim(), addToHistory: true);
                e.Handled = true;
            }
        }

        private void FileNameBox_KeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AcceptSelection();
                e.Handled = true;
            }
        }

        private void Window_KeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }

        private void NavigateUp()
        {
            DirectoryInfo? parent =
                Directory.GetParent(_currentDirectory);

            if (parent != null)
            {
                NavigateTo(parent.FullName, addToHistory: true);
            }
        }

        private void MoveThroughHistory(
            int direction)
        {
            int targetIndex = _historyIndex + direction;

            if (targetIndex < 0 || targetIndex >= _history.Count)
            {
                return;
            }

            _historyIndex = targetIndex;
            NavigateTo(_history[_historyIndex], addToHistory: false);
            UpdateNavigationButtons();
        }

        private void RefreshCurrentDirectory()
        {
            if (!string.IsNullOrWhiteSpace(_currentDirectory))
            {
                NavigateTo(_currentDirectory, addToHistory: false);
            }
        }

        private void UpdateNavigationButtons()
        {
            BackButton.IsEnabled = _historyIndex > 0;
            UpButton.IsEnabled = Directory.GetParent(_currentDirectory) != null;
        }

        private void AcceptSelection()
        {
            if (_mode == LimelightPickerMode.SelectFolder)
            {
                string selectedFolder =
                    EntryList.SelectedItem is PickerEntry entry && entry.IsDirectory
                        ? entry.FullPath
                        : _currentDirectory;

                SelectedPaths = new[] { selectedFolder };
                DialogResult = true;
                return;
            }

            if (_mode == LimelightPickerMode.SaveFile)
            {
                string fileName = FileNameBox.Text.Trim();

                if (string.IsNullOrWhiteSpace(fileName) ||
                    fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    ShowStatus("ENTER A VALID FILE NAME", isError: true);
                    return;
                }

                if (Path.GetExtension(fileName).Length == 0 &&
                    _defaultExtension.Length > 0)
                {
                    fileName += _defaultExtension;
                }

                string savePath = Path.Combine(_currentDirectory, fileName);

                if (File.Exists(savePath))
                {
                    LimelightDialogChoice overwrite =
                        LimelightDialog.Open(
                            this,
                            "REPLACE THIS FILE?",
                            $"{fileName} already exists in this folder.",
                            LimelightDialogTone.Question,
                            primaryAction: "REPLACE FILE",
                            secondaryAction: "KEEP EXISTING",
                            eyebrow: "SAVE LOCATION");

                    if (overwrite != LimelightDialogChoice.Primary)
                    {
                        return;
                    }
                }

                SelectedPaths = new[] { savePath };
                DialogResult = true;
                return;
            }

            List<string> selectedFiles =
                EntryList.SelectedItems
                    .OfType<PickerEntry>()
                    .Where(entry => !entry.IsDirectory)
                    .Select(entry => entry.FullPath)
                    .ToList();

            if (selectedFiles.Count == 0)
            {
                ShowStatus("SELECT AT LEAST ONE FILE", isError: true);
                return;
            }

            if (_mode == LimelightPickerMode.OpenFile && selectedFiles.Count > 1)
            {
                selectedFiles = new List<string> { selectedFiles[0] };
            }

            SelectedPaths = selectedFiles;
            DialogResult = true;
        }

        private void ShowStatus(
            string message,
            bool isError)
        {
            SelectionHintText.Text = message;
            SelectionHintText.Foreground =
                (Brush)FindResource(
                    isError
                        ? "PinkBrush"
                        : "MutedTextBrush");
        }
    }
}
