using HarmonyPcTouchpad.Agent.Security;

namespace HarmonyPcTouchpad.Agent.Transport;

public sealed class ControllerLeaseManager
{
    private readonly object _gate = new();
    private Guid? _activeLeaseId;
    private string? _activeDeviceId;

    public string? ActiveDeviceId
    {
        get
        {
            lock (_gate)
            {
                return _activeDeviceId;
            }
        }
    }

    public bool TryAcquire(string deviceId, out ControllerLease? lease)
    {
        if (!SecurityIdentifiers.IsValid(deviceId))
        {
            throw new ArgumentException("Device ID is invalid.", nameof(deviceId));
        }

        lock (_gate)
        {
            if (_activeLeaseId is not null)
            {
                lease = null;
                return false;
            }

            Guid leaseId = Guid.NewGuid();
            _activeLeaseId = leaseId;
            _activeDeviceId = deviceId;
            lease = new(this, leaseId, deviceId);
            return true;
        }
    }

    internal void Release(Guid leaseId)
    {
        lock (_gate)
        {
            if (_activeLeaseId != leaseId)
            {
                return;
            }

            _activeLeaseId = null;
            _activeDeviceId = null;
        }
    }
}

public sealed class ControllerLease : IDisposable
{
    private ControllerLeaseManager? _owner;
    private readonly Guid _leaseId;

    internal ControllerLease(
        ControllerLeaseManager owner,
        Guid leaseId,
        string deviceId)
    {
        _owner = owner;
        _leaseId = leaseId;
        DeviceId = deviceId;
    }

    public string DeviceId { get; }

    public void Dispose() =>
        Interlocked.Exchange(ref _owner, null)?.Release(_leaseId);
}
