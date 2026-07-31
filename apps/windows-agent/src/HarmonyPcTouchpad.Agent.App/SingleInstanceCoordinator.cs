namespace HarmonyPcTouchpad.Agent.App;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    public const string DefaultInstanceId =
        "CCDawn.HarmonyPcTouchpad.Agent";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private readonly bool _ownsMutex;
    private RegisteredWaitHandle? _activationRegistration;
    private bool _disposed;

    public SingleInstanceCoordinator(
        string instanceId = DefaultInstanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        string mutexName = $@"Local\{instanceId}.Mutex";
        string activationName = $@"Local\{instanceId}.Activate";
        _mutex = new Mutex(
            initiallyOwned: true,
            mutexName,
            out bool createdNew);
        _ownsMutex = createdNew;
        IsPrimary = createdNew;
        _activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            activationName);
    }

    public bool IsPrimary { get; }

    public void StartListening(Action activationHandler)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(activationHandler);
        if (!IsPrimary)
        {
            throw new InvalidOperationException(
                "Only the primary instance can listen for activation.");
        }

        StopListening();
        _activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, timedOut) =>
            {
                if (!timedOut)
                {
                    activationHandler();
                }
            },
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void StopListening()
    {
        RegisteredWaitHandle? registration =
            Interlocked.Exchange(ref _activationRegistration, null);
        registration?.Unregister(waitObject: null);
    }

    public void SignalPrimary()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _activationEvent.Set();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopListening();
        _activationEvent.Dispose();
        if (_ownsMutex)
        {
            _mutex.ReleaseMutex();
        }
        _mutex.Dispose();
    }
}
