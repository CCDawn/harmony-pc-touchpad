using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace HarmonyPcTouchpad.Agent.Transport;

public static class PrivateNetworkAddressDiscovery
{
    public static IReadOnlyList<IPAddress> Discover() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(network =>
                network.OperationalStatus == OperationalStatus.Up &&
                network.NetworkInterfaceType is not (
                    NetworkInterfaceType.Loopback or
                    NetworkInterfaceType.Tunnel))
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Select(unicast => unicast.Address)
            .Where(PrivateNetworkAddressPolicy.IsAllowed)
            .Distinct()
            .OrderBy(address =>
                address.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
            .ThenBy(address => address.ToString(), StringComparer.Ordinal)
            .ToArray();
}
