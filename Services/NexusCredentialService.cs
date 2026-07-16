using System;
using System.Security.Cryptography;
using System.Text;

namespace Limelight.Services
{
    public sealed class NexusCredentialService
    {
        private static readonly byte[] LimelightEntropy =
            Encoding.UTF8.GetBytes(
                "Limelight.Nexus.ApiKey.v1");

        public string Protect(
            string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return string.Empty;
            }

            byte[] plainBytes =
                Encoding.UTF8.GetBytes(
                    apiKey.Trim());

            // CurrentUser means another Windows account cannot simply copy
            // Limelight's settings file and recover the Nexus key.
            byte[] protectedBytes =
                ProtectedData.Protect(
                    plainBytes,
                    LimelightEntropy,
                    DataProtectionScope.CurrentUser);

            return Convert.ToBase64String(
                protectedBytes);
        }

        public string Unprotect(
            string protectedApiKey)
        {
            if (string.IsNullOrWhiteSpace(
                    protectedApiKey))
            {
                return string.Empty;
            }

            try
            {
                byte[] protectedBytes =
                    Convert.FromBase64String(
                        protectedApiKey);

                byte[] plainBytes =
                    ProtectedData.Unprotect(
                        protectedBytes,
                        LimelightEntropy,
                        DataProtectionScope.CurrentUser);

                return Encoding.UTF8.GetString(
                    plainBytes);
            }
            catch (FormatException)
            {
                return string.Empty;
            }
            catch (CryptographicException)
            {
                // A copied or damaged settings value is treated as disconnected.
                return string.Empty;
            }
        }
    }
}