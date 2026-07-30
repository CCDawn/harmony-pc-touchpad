using HarmonyPcTouchpad.Agent.Security;
using HarmonyPcTouchpad.Agent.Transport;
using HarmonyPcTouchpad.Agent.Windows;

namespace HarmonyPcTouchpad.Agent.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        try
        {
            Run();
        }
        catch (Exception error)
        {
            MessageBox.Show(
                $"Harmony PC Touchpad Agent 无法启动：{error.Message}",
                "Harmony PC Touchpad Agent",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void Run()
    {
        string dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HarmonyPcTouchpad");
        var protector = new DpapiSecretProtector();
        using WindowsAgentIdentity identity =
            new WindowsAgentIdentityStore(
                Path.Combine(dataRoot, "identity.json"),
                protector)
            .LoadOrCreate();
        var credentials = new WindowsDeviceCredentialStore(
            Path.Combine(dataRoot, "devices.json"),
            protector);
        var random = new SystemSecureRandom();
        var tickets = new PairingTicketService(TimeProvider.System, random);
        var pairingAuthority = new PairingAuthority(tickets, credentials, random);
        var authenticator = new RequestAuthenticator(
            identity.AgentId,
            credentials,
            TimeProvider.System,
            RequestAuthenticator.AllowedClockSkew,
            RequestAuthenticator.ReplayLifetime);
        IReadOnlyList<System.Net.IPAddress> addresses =
            PrivateNetworkAddressDiscovery.Discover();
        if (addresses.Count == 0)
        {
            throw new InvalidOperationException(
                "未找到可安全绑定的 RFC1918 或 IPv6 ULA 私有网络地址。");
        }

        var inputSink = new WindowsInputSink(new NativeWindowsInputApi());
        var host = new AgentWebSocketHost(
            identity.AgentId,
            identity.Certificate,
            addresses,
            pairingAuthority,
            authenticator,
            inputSink);
        bool hostOwnedByContext = false;
        try
        {
            host.StartAsync().GetAwaiter().GetResult();
            string CreatePairingPayload()
            {
                PairingTicket ticket = tickets.Issue();
                return PairingQrCodec.Encode(new(
                    1,
                    identity.AgentId,
                    new Uri(
                        $"wss://{identity.HostName}:{TransportPolicy.Port}/pair"),
                    CertificateFingerprint.ComputeSpkiSha256(identity.Certificate),
                    ticket.Token,
                    ticket.ExpiresAt.ToUnixTimeMilliseconds()));
            }

            using var context = new AgentApplicationContext(
                inputSink,
                host,
                addresses,
                CreatePairingPayload);
            hostOwnedByContext = true;
            Application.Run(context);
        }
        catch
        {
            if (!hostOwnedByContext)
            {
                host.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            throw;
        }
    }
}
