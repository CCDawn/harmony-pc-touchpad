using HarmonyPcTouchpad.Agent.Security;
using HarmonyPcTouchpad.Agent.Transport;
using HarmonyPcTouchpad.Agent.Windows;

namespace HarmonyPcTouchpad.Agent.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            using var instance = new SingleInstanceCoordinator();
            if (!instance.IsPrimary)
            {
                instance.SignalPrimary();
                return;
            }

            Run(AgentStartupOptions.Parse(args), instance);
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

    private static void Run(
        AgentStartupOptions options,
        SingleInstanceCoordinator instance)
    {
        string dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HarmonyPcTouchpad");
        var protector = new DpapiSecretProtector();
        IReadOnlyList<PrivateNetworkBinding> bindings =
            PrivateNetworkAddressDiscovery.DiscoverBindings();
        if (bindings.Count == 0)
        {
            throw new InvalidOperationException(
                "未找到可安全绑定的 RFC1918 或 IPv6 ULA 私有网络地址。");
        }
        System.Net.IPAddress[] addresses = bindings
            .Select(binding => binding.Address)
            .Distinct()
            .ToArray();
        using WindowsAgentIdentity identity =
            new WindowsAgentIdentityStore(
                Path.Combine(dataRoot, "identity.json"),
                protector)
            .LoadOrCreate(addresses);
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
        Uri pairingEndpoint = PairingEndpointSelector.Create(
            addresses,
            TransportPolicy.Port);

        var inputSink = new WindowsInputSink(new NativeWindowsInputApi());
        var host = new AgentWebSocketHost(
            identity.AgentId,
            identity.Certificate,
            addresses,
            pairingAuthority,
            authenticator,
            inputSink);
        var advertiser = new WindowsMdnsAdvertiser(
            identity.AgentId,
            identity.HostName,
            Environment.MachineName,
            pairingAllowed: false);
        bool hostOwnedByContext = false;
        try
        {
            host.StartAsync().GetAwaiter().GetResult();
            advertiser.StartAsync(
                    bindings.Select(binding => binding.InterfaceIndex))
                .GetAwaiter()
                .GetResult();
            PairingDisplayContent CreatePairingContent()
            {
                PairingTicket ticket = tickets.Issue();
                string payload = PairingQrCodec.Encode(new(
                    1,
                    identity.AgentId,
                    pairingEndpoint,
                    CertificateFingerprint.ComputeSpkiSha256(identity.Certificate),
                    ticket.Token,
                    ticket.ExpiresAt.ToUnixTimeMilliseconds()));
                return new(payload, ticket.ExpiresAt);
            }

            using var context = new AgentApplicationContext(
                inputSink,
                host,
                advertiser,
                addresses,
                CreatePairingContent,
                options.ShowPairing);
            hostOwnedByContext = true;
            instance.StartListening(context.RequestShowPairingCode);
            try
            {
                Application.Run(context);
            }
            finally
            {
                instance.StopListening();
            }
        }
        catch (Exception startupError)
        {
            if (!hostOwnedByContext)
            {
                var cleanupErrors = new List<Exception>();
                try
                {
                    advertiser.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch (Exception cleanupError)
                {
                    cleanupErrors.Add(cleanupError);
                }

                try
                {
                    host.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch (Exception cleanupError)
                {
                    cleanupErrors.Add(cleanupError);
                }

                if (cleanupErrors.Count > 0)
                {
                    throw new AggregateException(
                        "Agent startup and cleanup both failed.",
                        [startupError, .. cleanupErrors]);
                }
            }

            throw;
        }
    }
}
