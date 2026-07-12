using Limelight.Models;
using System.IO;
using System.Text.Json;

namespace Limelight.Services
{
    public sealed class SettingsService
    {
        private readonly string _settingsFolder;
        private readonly string _settingsFile;

        public SettingsService()
        {
            // LocalAppData is writable without administrator permissions and keeps
            // personal settings separate from the application's installation files. Mwuhaha
            _settingsFolder = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "Limelight");

            _settingsFile = Path.Combine(
                _settingsFolder,
                "settings.json");
        }

        public AppSettings Load()
        {
            // A missing file simply means this is the user's first launch. DUH
            if (!File.Exists(_settingsFile))
            {
                return new AppSettings();
            }

            try
            {
                string json = File.ReadAllText(_settingsFile);

                return JsonSerializer.Deserialize<AppSettings>(json)
                       ?? new AppSettings();
            }
            catch (IOException)
            {
                // If Windows cannot read the file, start with safe defaults.
                return new AppSettings();
            }
            catch (JsonException)
            {
                // A damaged settings file should not prevent Limelight from opening.
                return new AppSettings();
            }
        }

        public void Save(AppSettings settings)
        {
            Directory.CreateDirectory(_settingsFolder);

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(
                settings,
                jsonOptions);

            // Write to a temporary file first so an interrupted save does not
            // leave the main settings file only partially written.
            string temporaryFile = _settingsFile + ".tmp";

            File.WriteAllText(temporaryFile, json);
            File.Move(temporaryFile, _settingsFile, true);
        }
    }
}