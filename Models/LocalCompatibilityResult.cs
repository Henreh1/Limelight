namespace Limelight.Models
{
    public sealed class LocalCompatibilityResult
    {
        public string LimelightVersion { get; init; } =
            "UNKNOWN";

        public string SupportedSteamBuildId { get; init; } =
            string.Empty;

        public string SupportedGameVersion { get; init; } =
            string.Empty;

        public string DetectedSteamBuildId { get; init; } =
            string.Empty;

        public string DetectedGameVersion { get; init; } =
            string.Empty;

        public string NativeBridgeVersion { get; init; } =
            "UNKNOWN";

        public string Ue4ssVersion { get; init; } =
            "UNKNOWN";

        public bool GameConnected { get; init; }

        public bool GameBuildDetected { get; init; }

        public bool GameBuildCompatible { get; init; }

        public bool EmbeddedPayloadCompatible { get; init; }

        public bool Ue4ssInstalled { get; init; }

        public bool Ue4ssCompatible { get; init; }

        public bool Ue4ssConfigured { get; init; }

        public bool LuaBridgeInstalled { get; init; }

        public bool NativeBridgeCurrent { get; init; }

        public bool IsLiveLoaderCompatible =>
            GameConnected &&
            EmbeddedPayloadCompatible &&
            Ue4ssInstalled &&
            Ue4ssCompatible &&
            Ue4ssConfigured &&
            LuaBridgeInstalled &&
            NativeBridgeCurrent;

        public string DetectedGameLabel
        {
            get
            {
                if (!GameBuildDetected)
                {
                    return "UNKNOWN BUILD";
                }

                string steamBuild =
                    string.IsNullOrWhiteSpace(DetectedSteamBuildId)
                        ? "STEAM BUILD UNKNOWN"
                        : $"STEAM BUILD {DetectedSteamBuildId}";

                string gameVersion =
                    string.IsNullOrWhiteSpace(DetectedGameVersion)
                        ? string.Empty
                        : $" / {DetectedGameVersion}";

                return steamBuild + gameVersion;
            }
        }

        public string Status
        {
            get
            {
                if (!GameConnected)
                {
                    return "NOT CHECKED";
                }

                if (!GameBuildDetected)
                {
                    return "BUILD UNKNOWN";
                }

                if (!GameBuildCompatible)
                {
                    return "GAME UPDATE DETECTED";
                }

                return IsLiveLoaderCompatible
                    ? "COMPATIBLE"
                    : "REPAIR NEEDED";
            }
        }

        public string Detail
        {
            get
            {
                if (!GameConnected)
                {
                    return "Connect Dead as Disco to check the game and managed Live Loader files.";
                }

                if (!GameBuildDetected)
                {
                    return "Limelight could not identify this Dead as Disco build. The build number is advisory; launch and Live Loader readiness use the installed runtime and bridge checks instead.";
                }

                if (!GameBuildCompatible)
                {
                    return $"Dead as Disco has updated. Limelight previously verified Steam build {SupportedSteamBuildId} ({SupportedGameVersion}) and found {DetectedGameLabel}; this warning will not block launch or Live Loader repair.";
                }

                if (!EmbeddedPayloadCompatible)
                {
                    return "This Limelight build contains an invalid or incompatible native bridge payload.";
                }

                if (!Ue4ssInstalled)
                {
                    return "The compatible UE4SS runtime is not installed. Use Repair Live Loader below.";
                }

                if (!Ue4ssCompatible)
                {
                    return "The installed UE4SS runtime does not match Limelight's supported build.";
                }

                if (!Ue4ssConfigured)
                {
                    return "The Dead as Disco signatures or loader settings need to be refreshed.";
                }

                if (!LuaBridgeInstalled)
                {
                    return "Limelight's Lua bridge is missing or out of date.";
                }

                if (!NativeBridgeCurrent)
                {
                    return "The installed native bridge is missing or does not match this Limelight build.";
                }

                return "The game build, Limelight, UE4SS, and both bridge components match the supported local profile.";
            }
        }
    }
}
