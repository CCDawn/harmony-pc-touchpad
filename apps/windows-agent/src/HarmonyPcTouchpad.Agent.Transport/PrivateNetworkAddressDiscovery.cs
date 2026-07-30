using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace HarmonyPcTouchpad.Agent.Transport;

public sealed record PrivateNetworkBinding(
    IPAddress Address,
    uint InterfaceIndex);

public static class PrivateNetworkAddressDiscovery
{
    public static IReadOnlyList<IPAddress> Discover() =>
        DiscoverBindings()
            .Select(binding => binding.Address)
            .Distinct()
            .ToArray();

    public static IReadOnlyList<PrivateNetworkBinding> DiscoverBindings() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(network =>
                network.OperationalStatus == OperationalStatus.Up &&
                network.NetworkInterfaceType is not (
                    NetworkInterfaceType.Loopback or
                    NetworkInterfaceType.Tunnel))
            .SelectMany(CreateBindings)
            .Where(binding =>
                PrivateNetworkAddressPolicy.IsAllowed(binding.Address) &&
                binding.InterfaceIndex != 0)
            .Distinct()
            .OrderBy(binding =>
                binding.Address.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
            .ThenBy(
                binding => binding.Address.ToString(),
                StringComparer.Ordinal)
            .ThenBy(binding => binding.InterfaceIndex)
            .ToArray();

    private static IEnumerable<PrivateNetworkBinding> CreateBindings(
        NetworkInterface network)
    {
        IPInterfaceProperties properties = network.GetIPProperties();
        uint? ipv4Index = GetIpv4Index(properties);
        uint? ipv6Index = GetIpv6Index(properties);
        foreach (UnicastIPAddressInformation unicast in
                 properties.UnicastAddresses)
        {
            uint? interfaceIndex = unicast.Address.AddressFamily switch
            {
                AddressFamily.InterNetwork => ipv4Index,
                AddressFamily.InterNetworkV6 => ipv6Index,
                _ => null
            };
            if (interfaceIndex is not null)
            {
                yield return new(unicast.Address, interfaceIndex.Value);
            }
        }
    }

    private static uint? ToPositiveIndex(int? value) =>
        value is > 0 ? checked((uint)value.Value) : null;

    private static uint? GetIpv4Index(IPInterfaceProperties properties)
    {
        try
        {
            return ToPositiveIndex(properties.GetIPv4Properties()?.Index);
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    private static uint? GetIpv6Index(IPInterfaceProperties properties)
    {
        try
        {
            return ToPositiveIndex(properties.GetIPv6Properties()?.Index);
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }
}
