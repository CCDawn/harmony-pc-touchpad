using System.Net;
using System.Net.Sockets;

namespace HarmonyPcTouchpad.Agent.Transport;

public static class PrivateNetworkAddressPolicy
{
    public static bool IsAllowed(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            ReadOnlySpan<byte> bytes = address.GetAddressBytes();
            return bytes[0] == 10 ||
                bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
                bytes[0] == 192 && bytes[1] == 168;
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6 ||
            address.IsIPv4MappedToIPv6)
        {
            return false;
        }

        ReadOnlySpan<byte> ipv6 = address.GetAddressBytes();
        return (ipv6[0] & 0xFE) == 0xFC;
    }
}
