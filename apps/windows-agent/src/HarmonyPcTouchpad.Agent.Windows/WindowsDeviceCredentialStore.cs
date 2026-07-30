using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HarmonyPcTouchpad.Agent.Security;

namespace HarmonyPcTouchpad.Agent.Windows;

public interface ISecretProtector
{
    byte[] Protect(ReadOnlySpan<byte> plaintext);

    byte[] Unprotect(ReadOnlySpan<byte> protectedData);
}

public sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] AdditionalEntropy =
        SHA256.HashData("harmony-pc-touchpad/device-secret/v1"u8);

    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        byte[] copy = plaintext.ToArray();
        try
        {
            return ProtectedData.Protect(
                copy,
                AdditionalEntropy,
                DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(copy);
        }
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedData) =>
        ProtectedData.Unprotect(
            protectedData.ToArray(),
            AdditionalEntropy,
            DataProtectionScope.CurrentUser);
}

public sealed class WindowsDeviceCredentialStore :
    IDeviceCredentialStore,
    IDeviceCredentialWriter
{
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string _path;
    private readonly ISecretProtector _protector;

    public WindowsDeviceCredentialStore(string path, ISecretProtector protector)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Credential path is required.", nameof(path));
        }

        _path = Path.GetFullPath(path);
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    public void SaveSecret(string deviceId, ReadOnlySpan<byte> deviceSecret)
    {
        ValidateDeviceId(deviceId);
        if (deviceSecret.Length != PairingAuthority.DeviceSecretBytes)
        {
            throw new ArgumentException(
                $"Device secret must be {PairingAuthority.DeviceSecretBytes} bytes.",
                nameof(deviceSecret));
        }

        byte[] protectedSecret = _protector.Protect(deviceSecret);
        try
        {
            lock (_gate)
            {
                CredentialDocument document = Load();
                document.Credentials.RemoveAll(item => item.DeviceId == deviceId);
                document.Credentials.Add(new(
                    deviceId,
                    Convert.ToBase64String(protectedSecret)));
                Save(document);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedSecret);
        }
    }

    public bool TryGetSecret(string deviceId, out byte[] deviceSecret)
    {
        ValidateDeviceId(deviceId);
        lock (_gate)
        {
            CredentialRecord? record = Load().Credentials.SingleOrDefault(
                item => item.DeviceId == deviceId);
            if (record is null)
            {
                deviceSecret = [];
                return false;
            }

            byte[] protectedSecret;
            try
            {
                protectedSecret = Convert.FromBase64String(record.ProtectedSecret);
            }
            catch (FormatException error)
            {
                throw new InvalidDataException(
                    $"Credential for {deviceId} is not valid base64.",
                    error);
            }

            try
            {
                deviceSecret = _protector.Unprotect(protectedSecret);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedSecret);
            }

            if (deviceSecret.Length != PairingAuthority.DeviceSecretBytes)
            {
                CryptographicOperations.ZeroMemory(deviceSecret);
                deviceSecret = [];
                throw new InvalidDataException(
                    $"Credential for {deviceId} has an invalid secret length.");
            }

            return true;
        }
    }

    public bool DeleteSecret(string deviceId)
    {
        ValidateDeviceId(deviceId);
        lock (_gate)
        {
            CredentialDocument document = Load();
            int removed = document.Credentials.RemoveAll(
                item => item.DeviceId == deviceId);
            if (removed == 0)
            {
                return false;
            }

            Save(document);
            return true;
        }
    }

    private CredentialDocument Load()
    {
        if (!File.Exists(_path))
        {
            return new(SchemaVersion, []);
        }

        try
        {
            CredentialDocument document =
                JsonSerializer.Deserialize<CredentialDocument>(
                    File.ReadAllText(_path, Encoding.UTF8),
                    JsonOptions)
                ?? throw new InvalidDataException("Credential file is empty.");

            if (document.SchemaVersion != SchemaVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported credential schema version: {document.SchemaVersion}.");
            }

            if (document.Credentials is null ||
                document.Credentials.Any(item =>
                    !SecurityIdentifiers.IsValid(item.DeviceId) ||
                    !IsProtectedSecret(item.ProtectedSecret)) ||
                document.Credentials.Select(item => item.DeviceId).Distinct().Count() !=
                    document.Credentials.Count)
            {
                throw new InvalidDataException("Credential file contents are invalid.");
            }

            return document;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Credential file is not valid JSON.", error);
        }
    }

    private void Save(CredentialDocument document)
    {
        string? directory = Path.GetDirectoryName(_path);
        if (string.IsNullOrEmpty(directory))
        {
            throw new InvalidOperationException("Credential path has no parent directory.");
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(json);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void ValidateDeviceId(string deviceId)
    {
        if (!SecurityIdentifiers.IsValid(deviceId))
        {
            throw new ArgumentException("Device ID is invalid.", nameof(deviceId));
        }
    }

    private static bool IsProtectedSecret(string value)
    {
        try
        {
            return !string.IsNullOrEmpty(value) &&
                Convert.FromBase64String(value).Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed record CredentialDocument(
        int SchemaVersion,
        List<CredentialRecord> Credentials);

    private sealed record CredentialRecord(
        string DeviceId,
        string ProtectedSecret);
}
