using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace HarmonyPcTouchpad.Agent.Security;

public static class CertificateFingerprint
{
    public static string ComputeSpkiSha256(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        byte[] subjectPublicKeyInfo = certificate.PublicKey.ExportSubjectPublicKeyInfo();
        try
        {
            return Base64Url.Encode(SHA256.HashData(subjectPublicKeyInfo));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(subjectPublicKeyInfo);
        }
    }
}
