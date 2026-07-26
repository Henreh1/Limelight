using Limelight.Models;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace Limelight.Services
{
    public sealed class PrivateTestReportService
    {
        private static readonly HashSet<string> SanitizedTextExtensions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ".txt",
                ".log",
                ".json",
                ".ini",
                ".cfg",
                ".xml",
                ".csv"
            };

        public async Task CreateArchiveAsync(
            string destinationPath,
            PrivateTestReportRequest request,
            string automaticDiagnostics,
            string loaderMode,
            string? gameDirectory,
            string? nexusApiKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
            ArgumentNullException.ThrowIfNull(request);

            string temporaryArchive =
                destinationPath + ".building";

            File.Delete(temporaryArchive);

            try
            {
                await using FileStream archiveStream =
                    new(
                        temporaryArchive,
                        FileMode.CreateNew,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        81920,
                        useAsync: true);

                using var archive =
                    new ZipArchive(
                        archiveStream,
                        ZipArchiveMode.Create,
                        leaveOpen: false);

                string report =
                    BuildReport(
                        request,
                        automaticDiagnostics,
                        loaderMode);

                report = DiagnosticReportService.SanitizeText(
                    report,
                    gameDirectory,
                    nexusApiKey);

                await WriteTextEntryAsync(
                    archive,
                    "Limelight-Test-Report.txt",
                    report);

                await AddAttachmentsAsync(
                    archive,
                    request.AttachmentPaths,
                    gameDirectory,
                    nexusApiKey);
            }
            catch
            {
                File.Delete(temporaryArchive);
                throw;
            }

            File.Move(
                temporaryArchive,
                destinationPath,
                overwrite: true);
        }

        private static string BuildReport(
            PrivateTestReportRequest request,
            string automaticDiagnostics,
            string loaderMode)
        {
            Assembly? entryAssembly =
                Assembly.GetEntryAssembly();

            string version =
                entryAssembly?
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion ??
                entryAssembly?
                    .GetName()
                    .Version?
                    .ToString() ??
                "Unknown";

            using Process process =
                Process.GetCurrentProcess();

            var report =
                new StringBuilder();

            report.AppendLine("LIMELIGHT PRIVATE TEST REPORT");
            report.AppendLine("==============================");
            report.AppendLine($"Created (UTC): {DateTimeOffset.UtcNow:O}");
            report.AppendLine($"Limelight version: {version}");
            report.AppendLine($"Selected loader mode: {loaderMode}");
            report.AppendLine($"Limelight memory: {FormatBytes(process.WorkingSet64)}");
            report.AppendLine($"Peak Limelight memory: {FormatBytes(process.PeakWorkingSet64)}");
            report.AppendLine();

            report.AppendLine("TESTER NOTES");
            report.AppendLine($"Summary: {ValueOrNotProvided(request.Summary)}");
            report.AppendLine($"Area: {ValueOrNotProvided(request.Area)}");
            report.AppendLine($"Outcome: {ValueOrNotProvided(request.Outcome)}");
            report.AppendLine();
            AppendSection(
                report,
                "Steps to reproduce",
                request.ReproductionSteps);
            AppendSection(
                report,
                "Expected result",
                request.ExpectedResult);
            AppendSection(
                report,
                "Actual result",
                request.ActualResult);

            report.AppendLine("AUTOMATIC DIAGNOSTICS");
            report.AppendLine("---------------------");
            report.AppendLine(automaticDiagnostics.Trim());

            return report.ToString();
        }

        private static async Task AddAttachmentsAsync(
            ZipArchive archive,
            IReadOnlyList<string> attachmentPaths,
            string? gameDirectory,
            string? nexusApiKey)
        {
            int attachmentNumber = 0;

            foreach (string path in attachmentPaths
                         .Where(File.Exists)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .Take(10))
            {
                attachmentNumber++;

                string extension =
                    Path.GetExtension(path);

                string entryName =
                    $"Attachments/Attachment-{attachmentNumber:00}{extension.ToLowerInvariant()}";

                if (SanitizedTextExtensions.Contains(extension))
                {
                    string text =
                        await File.ReadAllTextAsync(path);

                    await WriteTextEntryAsync(
                        archive,
                        entryName,
                        DiagnosticReportService.SanitizeText(
                            text,
                            gameDirectory,
                            nexusApiKey));

                    continue;
                }

                ZipArchiveEntry entry =
                    archive.CreateEntry(
                        entryName,
                        CompressionLevel.Optimal);

                await using Stream destination =
                    entry.Open();

                await using FileStream source =
                    new(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite,
                        81920,
                        useAsync: true);

                await source.CopyToAsync(destination);
            }
        }

        private static async Task WriteTextEntryAsync(
            ZipArchive archive,
            string entryName,
            string contents)
        {
            ZipArchiveEntry entry =
                archive.CreateEntry(
                    entryName,
                    CompressionLevel.Optimal);

            await using Stream stream =
                entry.Open();

            await using var writer =
                new StreamWriter(
                    stream,
                    new UTF8Encoding(
                        encoderShouldEmitUTF8Identifier: false));

            await writer.WriteAsync(contents);
        }

        private static void AppendSection(
            StringBuilder report,
            string title,
            string value)
        {
            report.AppendLine(title.ToUpperInvariant());
            report.AppendLine(ValueOrNotProvided(value));
            report.AppendLine();
        }

        private static string ValueOrNotProvided(
            string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "Not provided"
                : value.Trim();
        }

        private static string FormatBytes(
            long bytes)
        {
            return $"{bytes / 1024d / 1024d:F1} MB";
        }
    }
}
