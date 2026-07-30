using Net.Codecrete.QrCodeGenerator;

namespace HarmonyPcTouchpad.Agent.App;

internal static class PairingQrImageRenderer
{
    private const int QuietZoneModules = 4;

    public static byte[] RenderPng(string payload, int pixelsPerModule)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new ArgumentException(
                "Pairing payload must not be blank.",
                nameof(payload));
        }

        if (pixelsPerModule < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pixelsPerModule),
                "Pixels per module must be positive.");
        }

        QrCode qr = QrCode.EncodeText(payload, QrCode.Ecc.Medium);
        return qr.ToPngBitmap(
            border: QuietZoneModules,
            scale: pixelsPerModule);
    }
}
