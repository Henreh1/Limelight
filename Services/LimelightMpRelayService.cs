using System.Diagnostics;
using System.IO;

namespace Limelight.Services
{
    public sealed class LimelightMpRelayService : IDisposable
    {
        private readonly object _gate =
            new();

        private Process? _process;

        public event Action<string>? OutputReceived;

        public event Action<int>? Exited;

        public bool IsRunning
        {
            get
            {
                lock (_gate)
                {
                    return
                        _process is not null &&
                        !_process.HasExited;
                }
            }
        }

        public void StartHost(
            string executablePath,
            int inputPort,
            string token)
        {
            Start(
                executablePath,
                new[]
                {
                    "--host",
                    "--port",
                    inputPort.ToString(),
                    "--token",
                    token
                });
        }

        public void StartClient(
            string executablePath,
            string hostAddress,
            int inputPort,
            string token)
        {
            Start(
                executablePath,
                new[]
                {
                    "--client",
                    "--address",
                    hostAddress,
                    "--port",
                    inputPort.ToString(),
                    "--token",
                    token
                });
        }

        public void Stop()
        {
            Process? process;

            lock (_gate)
            {
                process = _process;
                _process = null;
            }

            if (process is null)
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill(
                        entireProcessTree: true);

                    process.WaitForExit(3000);
                }
            }
            catch
            {
                // The process may have closed between the state check and kill.
            }
            finally
            {
                process.Dispose();
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private void Start(
            string executablePath,
            IEnumerable<string> arguments)
        {
            Stop();
            StopStaleManagedRelays(executablePath);

            ProcessStartInfo startInfo =
                new()
                {
                    FileName = executablePath,
                    WorkingDirectory =
                        Path.GetDirectoryName(executablePath) ??
                        string.Empty,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            Process process =
                new()
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };

            process.OutputDataReceived +=
                (_, eventArgs) =>
                {
                    if (!string.IsNullOrWhiteSpace(
                            eventArgs.Data))
                    {
                        OutputReceived?.Invoke(
                            eventArgs.Data);
                    }
                };

            process.ErrorDataReceived +=
                (_, eventArgs) =>
                {
                    if (!string.IsNullOrWhiteSpace(
                            eventArgs.Data))
                    {
                        OutputReceived?.Invoke(
                            $"[LimelightMP][ERROR] {eventArgs.Data}");
                    }
                };

            process.Exited +=
                (_, _) =>
                {
                    int exitCode = -1;

                    try
                    {
                        exitCode = process.ExitCode;
                    }
                    catch
                    {
                        // Keep the sentinel when Windows has already released it.
                    }

                    Exited?.Invoke(exitCode);
                };

            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException(
                    "Windows did not start the LimelightMP relay.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            lock (_gate)
            {
                _process = process;
            }

            Thread.Sleep(250);

            if (process.HasExited)
            {
                int exitCode = process.ExitCode;
                Stop();

                throw new InvalidOperationException(
                    $"The LimelightMP relay stopped during startup (exit code {exitCode}). " +
                    "Close another multiplayer session or allow the relay through Windows Firewall, then try again.");
            }
        }

        private static void StopStaleManagedRelays(
            string executablePath)
        {
            string expectedPath =
                Path.GetFullPath(executablePath);

            foreach (Process process in
                     Process.GetProcessesByName(
                         "LimelightMPRelay"))
            {
                try
                {
                    string? actualPath =
                        process.MainModule?.FileName;

                    if (!string.IsNullOrWhiteSpace(actualPath) &&
                        string.Equals(
                            Path.GetFullPath(actualPath),
                            expectedPath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        process.Kill(
                            entireProcessTree: true);

                        process.WaitForExit(2000);
                    }
                }
                catch
                {
                    // A stale process may close while it is being inspected.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
    }
}
