using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using HarmonyPcTouchpad.Agent.Protocol;
using HarmonyPcTouchpad.Agent.Security;

namespace HarmonyPcTouchpad.Agent.Windows;

internal sealed record WindowsMdnsRegistration(
    uint InterfaceIndex,
    string InstanceName,
    string HostName,
    ushort Port,
    IReadOnlyDictionary<string, string> Properties);

internal interface IWindowsDnsServiceRegistrationApi
{
    ValueTask<IAsyncDisposable> RegisterAsync(
        WindowsMdnsRegistration registration,
        CancellationToken cancellationToken);
}

public sealed class WindowsMdnsAdvertiser : IAsyncDisposable
{
    private readonly IWindowsDnsServiceRegistrationApi _api;
    private readonly WindowsMdnsRegistration _template;
    private readonly List<IAsyncDisposable> _registrations = [];
    private bool _started;
    private bool _disposed;

    public WindowsMdnsAdvertiser(
        string agentId,
        string hostName,
        string displayName,
        bool pairingAllowed)
        : this(
            new WindowsDnsServiceRegistrationApi(),
            agentId,
            hostName,
            displayName,
            pairingAllowed)
    {
    }

    internal WindowsMdnsAdvertiser(
        IWindowsDnsServiceRegistrationApi api,
        string agentId,
        string hostName,
        string displayName,
        bool pairingAllowed)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        if (!SecurityIdentifiers.IsValid(agentId))
        {
            throw new ArgumentException("Agent ID is invalid.", nameof(agentId));
        }

        if (!string.Equals(
                hostName,
                $"{agentId}.local",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The mDNS host name must match the agent identity.",
                nameof(hostName));
        }

        if (string.IsNullOrWhiteSpace(displayName) ||
            displayName.Length > 128 ||
            displayName.Any(char.IsControl))
        {
            throw new ArgumentException(
                "The mDNS display name is invalid.",
                nameof(displayName));
        }

        IReadOnlyDictionary<string, string> properties =
            new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["v"] = "1",
                    ["id"] = agentId,
                    ["name"] = displayName,
                    ["pairing"] = pairingAllowed ? "1" : "0"
                });
        _template = new(
            InterfaceIndex: 0,
            InstanceName:
                $"{agentId}.{DiscoveryServiceContract.QualifiedServiceType}",
            HostName: hostName,
            Port: DiscoveryServiceContract.Port,
            Properties: properties);
    }

    public async Task StartAsync(
        IEnumerable<uint> interfaceIndexes,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(interfaceIndexes);
        if (_started)
        {
            throw new InvalidOperationException(
                "The mDNS advertiser has already been started.");
        }

        uint[] indexes = interfaceIndexes.Distinct().ToArray();
        if (indexes.Length == 0 || indexes.Contains(0u))
        {
            throw new ArgumentException(
                "At least one explicit network-interface index is required.",
                nameof(interfaceIndexes));
        }

        _started = true;
        try
        {
            foreach (uint interfaceIndex in indexes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IAsyncDisposable registration = await _api.RegisterAsync(
                        _template with { InterfaceIndex = interfaceIndex },
                        cancellationToken)
                    .ConfigureAwait(false);
                _registrations.Add(registration);
            }
        }
        catch
        {
            await DisposeRegistrationsAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await DisposeRegistrationsAsync().ConfigureAwait(false);
    }

    private async ValueTask DisposeRegistrationsAsync()
    {
        List<Exception>? failures = null;
        for (int index = _registrations.Count - 1; index >= 0; index--)
        {
            try
            {
                await _registrations[index].DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                failures ??= [];
                failures.Add(error);
            }
        }

        _registrations.Clear();
        if (failures is { Count: 1 })
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures is { Count: > 1 })
        {
            throw new AggregateException(
                "One or more mDNS services could not be deregistered.",
                failures);
        }
    }
}
