using System.Net;
using System.Net.Sockets;

namespace HarmonyPcTouchpad.Agent.App;

internal static class PairingEndpointSelector
{
    public static Uri Create(
        IReadOnlyCollection<IPAddress> addresses,
        int port)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        if (addresses.Count == 0)
        {
            throw new ArgumentException(
                "At least one private network address is required.",
                nameof(addresses));
        }

        IPAddress selected = addresses.FirstOrDefault(
                address => address.AddressFamily == AddressFamily.InterNetwork)
            ?? addresses.First();
        return new UriBuilder(
            Uri.UriSchemeWss,
            selected.ToString(),
            port,
            "/pair").Uri;
    }
}
