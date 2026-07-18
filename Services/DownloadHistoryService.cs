using Limelight.Models;
using System.IO;
using System.Text.Json;

namespace Limelight.Services
{
    public sealed class DownloadHistoryService
    {
        private const int MaximumRecentDownloads = 50;

        private readonly string _historyFolder;
        private readonly string _historyFile;
        private readonly List<NexusDownloadRecord> _records =
            new();

        public DownloadHistoryService()
        {
            _historyFolder = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "Limelight");

            _historyFile = Path.Combine(
                _historyFolder,
                "download-history.json");

            Load();
        }

        public IReadOnlyList<NexusDownloadRecord> Records =>
            _records
                .OrderByDescending(record => record.IsActive)
                .ThenByDescending(record => record.StartedAt)
                .ToList();

        public NexusDownloadRecord Begin(
            NexusModFile file,
            string modName)
        {
            ArgumentNullException.ThrowIfNull(file);

            var record = new NexusDownloadRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                ModId = file.ModId,
                FileId = file.FileId,
                ModName = modName,
                FileName = file.FileName,
                Version = file.Version,
                Status = NexusDownloadStatus.Queued,
                TotalBytes = file.SizeKilobytes > 0
                    ? file.SizeKilobytes * 1024L
                    : null,
                StatusMessage = "Requesting a secure Nexus download.",
                StartedAt = DateTimeOffset.UtcNow
            };

            _records.Insert(0, record);
            TrimFinishedRecords();
            Save();

            return record;
        }

        public void ReportProgress(
            string recordId,
            NexusDownloadProgress progress)
        {
            NexusDownloadRecord? record =
                Find(recordId);

            if (record is null)
            {
                return;
            }

            record.Status =
                NexusDownloadStatus.Downloading;

            record.BytesReceived =
                progress.BytesReceived;

            if (progress.TotalBytes is > 0)
            {
                record.TotalBytes =
                    progress.TotalBytes;
            }

            record.StatusMessage =
                "Downloading and checking the archive.";
        }

        public void MarkInstalling(
            string recordId)
        {
            NexusDownloadRecord? record =
                Find(recordId);

            if (record is null)
            {
                return;
            }

            record.Status =
                NexusDownloadStatus.Installing;

            record.StatusMessage =
                "Validating and installing the mod.";

            Save();
        }

        public void MarkCompleted(
            string recordId,
            InstalledMod installedMod)
        {
            NexusDownloadRecord? record =
                Find(recordId);

            if (record is null)
            {
                return;
            }

            record.Status =
                NexusDownloadStatus.Completed;

            record.CompletedAt =
                DateTimeOffset.UtcNow;

            record.InstalledModId =
                installedMod.Id;

            record.StatusMessage =
                $"{installedMod.DisplayName} is ready in My Mods.";

            Save();
        }

        public void MarkFailed(
            string recordId,
            string message)
        {
            NexusDownloadRecord? record =
                Find(recordId);

            if (record is null)
            {
                return;
            }

            record.Status =
                NexusDownloadStatus.Failed;

            record.CompletedAt =
                DateTimeOffset.UtcNow;

            record.StatusMessage =
                string.IsNullOrWhiteSpace(message)
                    ? "The download could not be installed."
                    : message.Trim();

            Save();
        }

        public void ClearFinished()
        {
            _records.RemoveAll(record =>
                !record.IsActive);

            Save();
        }

        private NexusDownloadRecord? Find(
            string recordId)
        {
            return _records.FirstOrDefault(record =>
                string.Equals(
                    record.Id,
                    recordId,
                    StringComparison.OrdinalIgnoreCase));
        }

        private void Load()
        {
            if (!File.Exists(_historyFile))
            {
                return;
            }

            try
            {
                string json =
                    File.ReadAllText(_historyFile);

                List<NexusDownloadRecord>? savedRecords =
                    JsonSerializer.Deserialize<List<NexusDownloadRecord>>(
                        json);

                if (savedRecords is not null)
                {
                    _records.AddRange(savedRecords);
                }

                bool recoveredInterruptedDownload =
                    false;

                foreach (NexusDownloadRecord record in
                    _records.Where(record => record.IsActive))
                {
                    // Limelight cannot resume a Nexus link after restarting,
                    // so I keep the entry and make its interrupted state clear.
                    record.Status =
                        NexusDownloadStatus.Interrupted;

                    record.CompletedAt =
                        DateTimeOffset.UtcNow;

                    record.StatusMessage =
                        "Limelight closed before this download finished. Start it again from Browse Nexus.";

                    recoveredInterruptedDownload =
                        true;
                }

                TrimFinishedRecords();

                if (recoveredInterruptedDownload)
                {
                    Save();
                }
            }
            catch (IOException)
            {
                _records.Clear();
            }
            catch (UnauthorizedAccessException)
            {
                _records.Clear();
            }
            catch (JsonException)
            {
                _records.Clear();
            }
        }

        private void TrimFinishedRecords()
        {
            List<NexusDownloadRecord> active =
                _records
                    .Where(record => record.IsActive)
                    .ToList();

            List<NexusDownloadRecord> finished =
                _records
                    .Where(record => !record.IsActive)
                    .OrderByDescending(record => record.StartedAt)
                    .Take(MaximumRecentDownloads)
                    .ToList();

            _records.Clear();
            _records.AddRange(active);
            _records.AddRange(finished);
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(
                    _historyFolder);

                string json =
                    JsonSerializer.Serialize(
                        _records,
                        new JsonSerializerOptions
                        {
                            WriteIndented = true
                        });

                string temporaryFile =
                    _historyFile + ".tmp";

                File.WriteAllText(
                    temporaryFile,
                    json);

                File.Move(
                    temporaryFile,
                    _historyFile,
                    true);
            }
            catch (IOException)
            {
                // The live transfer should continue even if Windows briefly
                // prevents Limelight from updating its optional history file.
            }
            catch (UnauthorizedAccessException)
            {
                // History is helpful, but it must never block a valid download.
            }
        }
    }
}
