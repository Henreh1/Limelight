using Limelight.Models;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Limelight.Services
{
    public sealed partial class LimelightMpFriendCodeService
    {
        private const byte FormatVersion = 1;
        private const int TokenByteCount = 16;
        private const int PayloadByteCount = 23;

        public MultiplayerFriendConnection Create(
            string address,
            int inputPort)
        {
            byte[] token =
                RandomNumberGenerator.GetBytes(
                    TokenByteCount);

            return new MultiplayerFriendConnection
            {
                Address = ValidateAddress(address).ToString(),
                InputPort = ValidatePort(inputPort),
                Token = Convert.ToHexString(token).ToLowerInvariant()
            };
        }

        public string Encode(
            MultiplayerFriendConnection connection)
        {
            IPAddress address =
                ValidateAddress(connection.Address);

            int port =
                ValidatePort(connection.InputPort);

            byte[] token =
                Convert.FromHexString(
                    connection.Token);

            if (token.Length != TokenByteCount)
            {
                throw new InvalidOperationException(
                    "The LimelightMP relay token is invalid.");
            }

            byte[] payload =
                new byte[PayloadByteCount];

            payload[0] = FormatVersion;
            address.GetAddressBytes().CopyTo(payload, 1);
            payload[5] = (byte)((port >> 8) & 0xff);
            payload[6] = (byte)(port & 0xff);
            token.CopyTo(payload, 7);

            return
                "L1-" +
                Convert.ToBase64String(payload)
                    .TrimEnd('=')
                    .Replace('+', '-')
                    .Replace('/', '_');
        }

        public MultiplayerFriendConnection Decode(
            string code)
        {
            string trimmed =
                code?.Trim() ?? string.Empty;

            Match shortCode =
                ShortCodePattern().Match(trimmed);

            if (shortCode.Success)
            {
                byte[] payload =
                    DecodeBase64Url(
                        shortCode.Groups["payload"].Value);

                if (payload.Length != PayloadByteCount ||
                    payload[0] != FormatVersion)
                {
                    throw new InvalidOperationException(
                        "That LimelightMP code uses an unsupported format.");
                }

                int inputPort =
                    (payload[5] << 8) |
                    payload[6];

                ValidatePort(inputPort);

                return new MultiplayerFriendConnection
                {
                    Address =
                        new IPAddress(
                            payload[1..5])
                            .ToString(),
                    InputPort = inputPort,
                    Token =
                        Convert.ToHexString(
                            payload[7..23])
                            .ToLowerInvariant()
                };
            }

            Match legacy =
                LegacyCodePattern().Match(trimmed);

            if (legacy.Success)
            {
                int inputPort =
                    ValidatePort(
                        int.Parse(
                            legacy.Groups["port"].Value));

                return new MultiplayerFriendConnection
                {
                    Address =
                        legacy.Groups["address"].Value,
                    InputPort = inputPort,
                    Token =
                        legacy.Groups["token"].Value
                            .ToLowerInvariant()
                };
            }

            throw new InvalidOperationException(
                "That LimelightMP code is invalid. Copy the complete L1- code from the host.");
        }

        private static IPAddress ValidateAddress(
            string address)
        {
            if (!IPAddress.TryParse(
                    address,
                    out IPAddress? parsed) ||
                parsed.AddressFamily !=
                    AddressFamily.InterNetwork)
            {
                throw new InvalidOperationException(
                    "Short LimelightMP codes require an IPv4 or Tailscale address.");
            }

            return parsed;
        }

        private static int ValidatePort(
            int port)
        {
            if (port <= 1024 || port > 65535)
            {
                throw new InvalidOperationException(
                    "The LimelightMP code contains an invalid input port.");
            }

            return port;
        }

        private static byte[] DecodeBase64Url(
            string value)
        {
            string base64 =
                value
                    .Replace('-', '+')
                    .Replace('_', '/');

            base64 +=
                (base64.Length % 4) switch
                {
                    0 => string.Empty,
                    2 => "==",
                    3 => "=",
                    _ => throw new InvalidOperationException(
                        "The LimelightMP code has invalid padding.")
                };

            try
            {
                return Convert.FromBase64String(base64);
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException(
                    "The LimelightMP code contains invalid characters.",
                    exception);
            }
        }

        [GeneratedRegex(
            "^L1-(?<payload>[A-Za-z0-9_-]{31})$",
            RegexOptions.CultureInvariant)]
        private static partial Regex ShortCodePattern();

        [GeneratedRegex(
            "^(?<address>[A-Za-z0-9.-]+):(?<port>[0-9]{4,5})#(?<token>[0-9A-Fa-f]{32})$",
            RegexOptions.CultureInvariant)]
        private static partial Regex LegacyCodePattern();
    }
}
