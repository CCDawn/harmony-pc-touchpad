using HarmonyPcTouchpad.Agent.Protocol;

namespace HarmonyPcTouchpad.Agent.Windows.Tests;

public sealed class WindowsMdnsAdvertiserTests
{
    [Fact]
    public async Task StartRegistersOnePrivateServicePerUniqueInterface()
    {
        var api = new RecordingDnsServiceRegistrationApi();
        await using var advertiser = new WindowsMdnsAdvertiser(
            api,
            "agent-001",
            "agent-001.local",
            "Living Room PC",
            pairingAllowed: false);

        await advertiser.StartAsync([12, 12, 27], CancellationToken.None);

        Assert.Equal([12u, 27u], api.Registrations.Select(x => x.InterfaceIndex));
        Assert.All(
            api.Registrations,
            registration =>
            {
                Assert.Equal(
                    $"agent-001.{DiscoveryServiceContract.QualifiedServiceType}",
                    registration.InstanceName);
                Assert.Equal("agent-001.local", registration.HostName);
                Assert.Equal(47431, registration.Port);
                Assert.Equal(
                    new Dictionary<string, string>
                    {
                        ["v"] = "1",
                        ["id"] = "agent-001",
                        ["name"] = "Living Room PC",
                        ["pairing"] = "0"
                    },
                    registration.Properties);
            });
    }

    [Fact]
    public async Task DisposeDeregistersEverySuccessfulRegistration()
    {
        var api = new RecordingDnsServiceRegistrationApi();
        var advertiser = new WindowsMdnsAdvertiser(
            api,
            "agent-001",
            "agent-001.local",
            "Living Room PC",
            pairingAllowed: true);
        await advertiser.StartAsync([12, 27], CancellationToken.None);

        await advertiser.DisposeAsync();

        Assert.All(api.Handles, handle => Assert.True(handle.Disposed));
    }

    [Fact]
    public async Task PartialStartFailureRollsBackEarlierRegistrations()
    {
        var api = new RecordingDnsServiceRegistrationApi(failInterfaceIndex: 27);
        await using var advertiser = new WindowsMdnsAdvertiser(
            api,
            "agent-001",
            "agent-001.local",
            "Living Room PC",
            pairingAllowed: false);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => advertiser.StartAsync([12, 27], CancellationToken.None));

        Assert.True(api.Handles.Single().Disposed);
    }

    [Fact]
    public async Task DisposeAttemptsEveryRegistrationWhenOneDeregistrationFails()
    {
        var api = new RecordingDnsServiceRegistrationApi(
            failDisposeInterfaceIndex: 27);
        var advertiser = new WindowsMdnsAdvertiser(
            api,
            "agent-001",
            "agent-001.local",
            "Living Room PC",
            pairingAllowed: false);
        await advertiser.StartAsync([12, 27], CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => advertiser.DisposeAsync().AsTask());

        Assert.All(api.Handles, handle => Assert.True(handle.Disposed));
    }

    private sealed class RecordingDnsServiceRegistrationApi(
        uint? failInterfaceIndex = null,
        uint? failDisposeInterfaceIndex = null) :
        IWindowsDnsServiceRegistrationApi
    {
        public List<WindowsMdnsRegistration> Registrations { get; } = [];

        public List<RecordingRegistrationHandle> Handles { get; } = [];

        public ValueTask<IAsyncDisposable> RegisterAsync(
            WindowsMdnsRegistration registration,
            CancellationToken cancellationToken)
        {
            if (registration.InterfaceIndex == failInterfaceIndex)
            {
                throw new InvalidOperationException("Registration failed.");
            }

            Registrations.Add(registration);
            var handle = new RecordingRegistrationHandle(
                registration.InterfaceIndex == failDisposeInterfaceIndex);
            Handles.Add(handle);
            return ValueTask.FromResult<IAsyncDisposable>(handle);
        }
    }

    private sealed class RecordingRegistrationHandle(bool failOnDispose) :
        IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            if (failOnDispose)
            {
                throw new InvalidOperationException("Deregistration failed.");
            }

            return ValueTask.CompletedTask;
        }
    }
}
