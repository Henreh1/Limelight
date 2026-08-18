using DiscordRPC;
using DiscordRPC.Logging;
using Limelight.Models;

namespace Limelight.Services
{
    public sealed class DiscordPresenceService : IDisposable
    {
        private const string ApplicationId =
            "1528049640057081917";

        private DiscordRpcClient? _client;
        private DateTime _applicationStartedUtc =
            DateTime.UtcNow;
        private DateTime? _gameStartedUtc;
        private bool _wasGameRunning;
        private string _lastPresenceSignature =
            string.Empty;

        public bool IsEnabled { get; private set; }

        public void SetEnabled(
            bool enabled)
        {
            if (enabled == IsEnabled)
            {
                return;
            }

            IsEnabled = enabled;

            if (!enabled)
            {
                StopClient();
                return;
            }

            StartClient();
        }

        public void Update(
            bool isGameRunning,
            bool isSwitching,
            string navigationLabel,
            string? activeModName,
            string loaderMode,
            string? switchingToModName,
            MultiplayerRole multiplayerRole)
        {
            if (!IsEnabled)
            {
                return;
            }

            if (_client is null ||
                _client.IsDisposed)
            {
                StartClient();
            }

            if (_client is null)
            {
                return;
            }

            UpdateSessionClock(
                isGameRunning);

            string details =
                CreateDetails(
                    isGameRunning,
                    isSwitching,
                    navigationLabel,
                    multiplayerRole);

            string state =
                CreateState(
                    isGameRunning,
                    isSwitching,
                    activeModName,
                    loaderMode,
                    switchingToModName,
                    multiplayerRole);

            DateTime startedUtc =
                isGameRunning
                    ? _gameStartedUtc ?? DateTime.UtcNow
                    : _applicationStartedUtc;

            string signature =
                $"{details}\n{state}\n{startedUtc.Ticks}";

            if (string.Equals(
                    signature,
                    _lastPresenceSignature,
                    StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                _client.SetPresence(
                    new RichPresence
                    {
                        Type = ActivityType.Playing,
                        Details = Limit(details),
                        State = Limit(state),
                        Timestamps = new Timestamps(
                            startedUtc)
                    });

                _lastPresenceSignature =
                    signature;
            }
            catch
            {
                // Discord may be closed or restarting. Rich Presence is optional,
                // so I leave Limelight running and let the client reconnect later.
            }
        }

        private void StartClient()
        {
            StopClient();

            try
            {
                _client =
                    new DiscordRpcClient(
                        ApplicationId)
                    {
                        Logger = new NullLogger(),
                        SkipIdenticalPresence = true
                    };

                _client.Initialize();
                _lastPresenceSignature =
                    string.Empty;
            }
            catch
            {
                // A missing Discord desktop client is a normal state. The next
                // status refresh can quietly try again without interrupting users.
                StopClient();
            }
        }

        private void StopClient()
        {
            if (_client is null)
            {
                return;
            }

            try
            {
                _client.ClearPresence();
                _client.Dispose();
            }
            catch
            {
                // Discord may disappear before shutdown finishes. There is
                // nothing the user needs to repair inside Limelight.
            }
            finally
            {
                _client = null;
                _lastPresenceSignature =
                    string.Empty;
            }
        }

        private void UpdateSessionClock(
            bool isGameRunning)
        {
            if (isGameRunning &&
                !_wasGameRunning)
            {
                _gameStartedUtc =
                    DateTime.UtcNow;
            }
            else if (!isGameRunning &&
                     _wasGameRunning)
            {
                _gameStartedUtc =
                    null;
            }

            _wasGameRunning =
                isGameRunning;
        }

        private static string CreateDetails(
            bool isGameRunning,
            bool isSwitching,
            string navigationLabel,
            MultiplayerRole multiplayerRole)
        {
            if (multiplayerRole is
                MultiplayerRole.Host or
                MultiplayerRole.Client)
            {
                return isGameRunning
                    ? "Playing Dead as Disco Multiplayer"
                    : "Preparing Dead as Disco Multiplayer";
            }

            if (isSwitching)
            {
                return "Switching the spotlight";
            }

            return isGameRunning
                ? "Playing Dead as Disco"
                : navigationLabel;
        }

        private static string CreateState(
            bool isGameRunning,
            bool isSwitching,
            string? activeModName,
            string loaderMode,
            string? switchingToModName,
            MultiplayerRole multiplayerRole)
        {
            if (multiplayerRole is
                MultiplayerRole.Host or
                MultiplayerRole.Client)
            {
                string multiplayerState =
                    multiplayerRole == MultiplayerRole.Host
                        ? "Hosting online co-op"
                        : "Joined online co-op";

                if (isSwitching)
                {
                    return string.IsNullOrWhiteSpace(
                               switchingToModName)
                        ? $"{multiplayerState} · switching character"
                        : $"{multiplayerState} · next: {switchingToModName}";
                }

                return string.IsNullOrWhiteSpace(
                           activeModName)
                    ? multiplayerState
                    : $"{multiplayerState} · {activeModName}";
            }

            if (isSwitching)
            {
                return string.IsNullOrWhiteSpace(
                           switchingToModName)
                    ? loaderMode
                    : $"Next: {switchingToModName}";
            }

            if (!string.IsNullOrWhiteSpace(
                    activeModName))
            {
                return isGameRunning
                    ? $"Spotlight: {activeModName}"
                    : $"Active mod: {activeModName}";
            }

            return isGameRunning
                ? $"{loaderMode} ready"
                : "Preparing the next act";
        }

        private static string Limit(
            string value)
        {
            const int maximumLength = 128;

            return value.Length <= maximumLength
                ? value
                : value[..maximumLength];
        }

        public void Dispose()
        {
            StopClient();
        }
    }
}
