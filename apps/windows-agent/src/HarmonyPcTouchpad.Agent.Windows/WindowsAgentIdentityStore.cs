using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using HarmonyPcTouchpad.Agent.Security;

namespace HarmonyPcTouchpad.Agent.Windows;

public sealed class WindowsAgentIdentity : IDisposable
{
    internal WindowsAgentIdentity(string agentId, X509Certificate2 certificate)
    {
        AgentId = agentId;
        HostName = $"{agentId}.local";
        Certificate = certificate;
    }

    public string AgentId { get; }

    public string HostName { get; }

    public X509Certificate2 Certificate { get; }

    public void Dispose() => Certificate.Dispose();
}

public sealed class WindowsAgentIdentityStore
{
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _path;
    private readonly ISecretProtector _protector;
    private readonly TimeProvider _clock;

    public WindowsAgentIdentityStore(
        string path,
        ISecretProtector protector,
        TimeProvider? clock = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Identity path is required.", nameof(path));
        }

        _path = Path.GetFullPath(path);
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _clock = clock ?? TimeProvider.System;
    }

    public WindowsAgentIdentity LoadOrCreate() => LoadOrCreate([]);

    public WindowsAgentIdentity LoadOrCreate(
        IReadOnlyCollection<IPAddress> subjectAlternativeAddresses)
    {
        ArgumentNullException.ThrowIfNull(subjectAlternativeAddresses);
        IPAddress[] addresses = subjectAlternativeAddresses
            .Distinct()
            .ToArray();
        if (!File.Exists(_path))
        {
            return Create(agentId: null, addresses, overwrite: false);
        }

        WindowsAgentIdentity identity = Load();
        if (CertificateCovers(identity.Certificate, addresses))
        {
            return identity;
        }

        try
        {
            return Create(identity.AgentId, addresses, overwrite: true);
        }
        finally
        {
            identity.Dispose();
        }
    }

    private WindowsAgentIdentity Create(
        string? agentId,
        IReadOnlyCollection<IPAddress> subjectAlternativeAddresses,
        bool overwrite)
    {
        if (agentId is null)
        {
            byte[] identifierBytes = RandomNumberGenerator.GetBytes(16);
            try
            {
                agentId =
                    $"agent-{Convert.ToHexString(identifierBytes).ToLowerInvariant()}";
            }
            finally
            {
                CryptographicOperations.ZeroMemory(identifierBytes);
            }
        }

        string hostName = $"{agentId}.local";
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={hostName}",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(
            certificateAuthority: false,
            hasPathLengthConstraint: false,
            pathLengthConstraint: 0,
            critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature |
            X509KeyUsageFlags.KeyEncipherment,
            critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1")],
            critical: true));
        var subjectNames = new SubjectAlternativeNameBuilder();
        subjectNames.AddDnsName(hostName);
        foreach (IPAddress address in subjectAlternativeAddresses)
        {
            subjectNames.AddIpAddress(address);
        }

        request.CertificateExtensions.Add(subjectNames.Build());

        DateTimeOffset now = _clock.GetUtcNow();
        using X509Certificate2 generated = request.CreateSelfSigned(
            now.AddMinutes(-5),
            now.AddYears(5));
        byte[] pfx = generated.Export(X509ContentType.Pfx);
        try
        {
            X509Certificate2 certificate = LoadCertificate(pfx);
            try
            {
                Save(agentId, pfx, overwrite);
                return new(agentId, certificate);
            }
            catch
            {
                certificate.Dispose();
                throw;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pfx);
        }
    }

    private WindowsAgentIdentity Load()
    {
        IdentityDocument document;
        try
        {
            document = JsonSerializer.Deserialize<IdentityDocument>(
                    File.ReadAllText(_path, Encoding.UTF8),
                    JsonOptions)
                ?? throw new InvalidDataException("Identity file is empty.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Identity file is not valid JSON.", error);
        }

        if (document.SchemaVersion != SchemaVersion ||
            !SecurityIdentifiers.IsValid(document.AgentId) ||
            string.IsNullOrEmpty(document.ProtectedCertificate))
        {
            throw new InvalidDataException("Identity file contents are invalid.");
        }

        byte[] protectedCertificate;
        try
        {
            protectedCertificate =
                Convert.FromBase64String(document.ProtectedCertificate);
        }
        catch (FormatException error)
        {
            throw new InvalidDataException(
                "Protected certificate is not valid base64.",
                error);
        }

        try
        {
            byte[] pfx = _protector.Unprotect(protectedCertificate);
            try
            {
                X509Certificate2 certificate = LoadCertificate(pfx);
                try
                {
                    Validate(document.AgentId, certificate);
                    return new(document.AgentId, certificate);
                }
                catch
                {
                    certificate.Dispose();
                    throw;
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pfx);
            }
        }
        catch (CryptographicException error)
        {
            throw new InvalidDataException(
                "Stored agent certificate cannot be decrypted or loaded.",
                error);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedCertificate);
        }
    }

    private void Save(
        string agentId,
        ReadOnlySpan<byte> pfx,
        bool overwrite)
    {
        byte[] protectedCertificate = _protector.Protect(pfx);
        try
        {
            var document = new IdentityDocument(
                SchemaVersion,
                agentId,
                Convert.ToBase64String(protectedCertificate));
            string? directory = Path.GetDirectoryName(_path);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException(
                    "Identity path has no parent directory.");
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

                File.Move(temporaryPath, _path, overwrite);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedCertificate);
        }
    }

    private static X509Certificate2 LoadCertificate(ReadOnlySpan<byte> pfx) =>
        X509CertificateLoader.LoadPkcs12(
            pfx,
            password: null,
            X509KeyStorageFlags.UserKeySet);

    private static bool CertificateCovers(
        X509Certificate2 certificate,
        IReadOnlyCollection<IPAddress> requiredAddresses)
    {
        if (requiredAddresses.Count == 0)
        {
            return true;
        }

        X509Extension? extension = certificate.Extensions["2.5.29.17"];
        if (extension is null)
        {
            return false;
        }

        var subjectNames = new X509SubjectAlternativeNameExtension(
            extension.RawData,
            extension.Critical);
        HashSet<IPAddress> addresses =
            subjectNames.EnumerateIPAddresses().ToHashSet();
        return requiredAddresses.All(addresses.Contains);
    }

    private void Validate(string agentId, X509Certificate2 certificate)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        string expectedHostName = $"{agentId}.local";
        if (!certificate.HasPrivateKey ||
            certificate.NotBefore.ToUniversalTime() > now.UtcDateTime ||
            certificate.NotAfter.ToUniversalTime() <= now.UtcDateTime ||
            !string.Equals(
                certificate.GetNameInfo(X509NameType.DnsName, forIssuer: false),
                expectedHostName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Stored certificate is invalid, expired, or belongs to another agent.");
        }
    }

    private sealed record IdentityDocument(
        int SchemaVersion,
        string AgentId,
        string ProtectedCertificate);
}
