using System.ComponentModel;
using System.Runtime.InteropServices;

namespace HarmonyPcTouchpad.Agent.Windows;

internal sealed class WindowsDnsServiceRegistrationApi :
    IWindowsDnsServiceRegistrationApi
{
    public async ValueTask<IAsyncDisposable> RegisterAsync(
        WindowsMdnsRegistration registration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);
        cancellationToken.ThrowIfCancellationRequested();

        var nativeRegistration = new NativeDnsServiceRegistration(registration);
        try
        {
            await nativeRegistration.RegisterAsync(cancellationToken)
                .ConfigureAwait(false);
            return nativeRegistration;
        }
        catch
        {
            nativeRegistration.DisposeUnregistered();
            throw;
        }
    }

    private sealed class NativeDnsServiceRegistration : IAsyncDisposable
    {
        private const uint Success = 0;
        private const uint DnsRequestPending = 0x2522;
        private const uint ErrorCancelled = 1223;

        private static readonly DnsServiceRegisterComplete RegistrationCallback =
            OnRegistrationComplete;
        private static readonly IntPtr RegistrationCallbackPointer =
            Marshal.GetFunctionPointerForDelegate(RegistrationCallback);

        private readonly object _sync = new();
        private readonly List<IntPtr> _allocatedStrings = [];
        private readonly IntPtr _cancelHandle;
        private readonly IntPtr _instancePointer;
        private readonly GCHandle _selfHandle;
        private DnsServiceRegisterRequest _request;
        private TaskCompletionSource<uint>? _pendingCompletion;
        private bool _registered;
        private bool _disposed;

        public NativeDnsServiceRegistration(WindowsMdnsRegistration registration)
        {
            _instancePointer = CreateServiceInstance(registration);
            _cancelHandle = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(_cancelHandle, IntPtr.Zero);
            _selfHandle = GCHandle.Alloc(this);
            _request = new()
            {
                Version = 1,
                InterfaceIndex = registration.InterfaceIndex,
                ServiceInstance = _instancePointer,
                CompletionCallback = RegistrationCallbackPointer,
                QueryContext = GCHandle.ToIntPtr(_selfHandle),
                Credentials = IntPtr.Zero,
                UnicastEnabled = false
            };
        }

        public async Task RegisterAsync(CancellationToken cancellationToken)
        {
            Task<uint> completion;
            lock (_sync)
            {
                ThrowIfDisposed();
                _pendingCompletion = CreateCompletion();
                uint result = DnsServiceRegister(
                    ref _request,
                    _cancelHandle);
                if (result != DnsRequestPending)
                {
                    _pendingCompletion = null;
                    throw CreateNativeError("register", result);
                }

                completion = _pendingCompletion.Task;
            }

            using CancellationTokenRegistration cancellation =
                cancellationToken.Register(
                    static state =>
                        ((NativeDnsServiceRegistration)state!)
                        .CancelPendingRegistration(),
                    this);
            uint status = await completion.ConfigureAwait(false);
            if (status == ErrorCancelled &&
                cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (status != Success)
            {
                throw CreateNativeError("register", status);
            }

            lock (_sync)
            {
                _registered = true;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Task<uint>? completion = null;
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                if (_registered)
                {
                    _pendingCompletion = CreateCompletion();
                    uint result = DnsServiceDeRegister(
                        ref _request,
                        IntPtr.Zero);
                    if (result != DnsRequestPending)
                    {
                        _pendingCompletion = null;
                        DisposeNativeResources();
                        throw CreateNativeError("deregister", result);
                    }

                    completion = _pendingCompletion.Task;
                }
                else
                {
                    DisposeNativeResources();
                }
            }

            if (completion is not null)
            {
                uint status = await completion.ConfigureAwait(false);
                lock (_sync)
                {
                    DisposeNativeResources();
                }

                if (status is not (Success or ErrorCancelled))
                {
                    throw CreateNativeError("deregister", status);
                }
            }
        }

        public void DisposeUnregistered()
        {
            lock (_sync)
            {
                if (!_registered)
                {
                    DisposeNativeResources();
                }
            }
        }

        private static TaskCompletionSource<uint> CreateCompletion() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static Win32Exception CreateNativeError(
            string operation,
            uint status) =>
            new(
                unchecked((int)status),
                $"Windows DNS-SD failed to {operation} the mDNS service " +
                $"(status {status}).");

        private static void OnRegistrationComplete(
            uint status,
            IntPtr queryContext,
            IntPtr serviceInstance)
        {
            if (queryContext == IntPtr.Zero)
            {
                if (serviceInstance != IntPtr.Zero)
                {
                    DnsServiceFreeInstance(serviceInstance);
                }

                return;
            }

            var self = (NativeDnsServiceRegistration?)
                GCHandle.FromIntPtr(queryContext).Target;
            self?.Complete(status, serviceInstance);
        }

        private void Complete(uint status, IntPtr returnedInstance)
        {
            Exception? failure = null;
            try
            {
                if (status == Success && returnedInstance != IntPtr.Zero)
                {
                    UpdateRegisteredNames(returnedInstance);
                }
            }
            catch (Exception error)
            {
                failure = error;
            }
            finally
            {
                if (returnedInstance != IntPtr.Zero)
                {
                    DnsServiceFreeInstance(returnedInstance);
                }
            }

            TaskCompletionSource<uint>? completion;
            lock (_sync)
            {
                completion = _pendingCompletion;
                _pendingCompletion = null;
            }

            if (failure is not null)
            {
                completion?.TrySetException(failure);
            }
            else
            {
                completion?.TrySetResult(status);
            }
        }

        private void CancelPendingRegistration()
        {
            lock (_sync)
            {
                if (!_disposed && !_registered && _pendingCompletion is not null)
                {
                    _ = DnsServiceRegisterCancel(_cancelHandle);
                }
            }
        }

        private IntPtr CreateServiceInstance(
            WindowsMdnsRegistration registration)
        {
            IntPtr instanceName = AllocateString(registration.InstanceName);
            IntPtr hostName = AllocateString(registration.HostName);
            IntPtr keys = AllocateStringArray(registration.Properties.Keys);
            IntPtr values = AllocateStringArray(registration.Properties.Values);
            var instance = new DnsServiceInstance
            {
                InstanceName = instanceName,
                HostName = hostName,
                Ip4Address = IntPtr.Zero,
                Ip6Address = IntPtr.Zero,
                Port = registration.Port,
                Priority = 0,
                Weight = 0,
                PropertyCount = checked((uint)registration.Properties.Count),
                Keys = keys,
                Values = values,
                InterfaceIndex = registration.InterfaceIndex
            };
            IntPtr pointer =
                Marshal.AllocHGlobal(Marshal.SizeOf<DnsServiceInstance>());
            Marshal.StructureToPtr(instance, pointer, fDeleteOld: false);
            return pointer;
        }

        private IntPtr AllocateString(string value)
        {
            IntPtr pointer = Marshal.StringToHGlobalUni(value);
            _allocatedStrings.Add(pointer);
            return pointer;
        }

        private IntPtr AllocateStringArray(IEnumerable<string> values)
        {
            IntPtr[] pointers = values.Select(AllocateString).ToArray();
            IntPtr array = Marshal.AllocHGlobal(IntPtr.Size * pointers.Length);
            Marshal.Copy(pointers, 0, array, pointers.Length);
            _allocatedStrings.Add(array);
            return array;
        }

        private void UpdateRegisteredNames(IntPtr returnedInstance)
        {
            DnsServiceInstance registered =
                Marshal.PtrToStructure<DnsServiceInstance>(returnedInstance);
            DnsServiceInstance owned =
                Marshal.PtrToStructure<DnsServiceInstance>(_instancePointer);
            ReplaceString(
                ref owned.InstanceName,
                Marshal.PtrToStringUni(registered.InstanceName));
            ReplaceString(
                ref owned.HostName,
                Marshal.PtrToStringUni(registered.HostName));
            Marshal.StructureToPtr(owned, _instancePointer, fDeleteOld: false);
        }

        private void ReplaceString(ref IntPtr current, string? value)
        {
            if (string.IsNullOrEmpty(value) ||
                string.Equals(
                    Marshal.PtrToStringUni(current),
                    value,
                    StringComparison.Ordinal))
            {
                return;
            }

            Marshal.FreeHGlobal(current);
            _allocatedStrings.Remove(current);
            current = AllocateString(value);
        }

        private void ThrowIfDisposed() =>
            ObjectDisposedException.ThrowIf(_disposed, this);

        private void DisposeNativeResources()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (IntPtr pointer in _allocatedStrings)
            {
                Marshal.FreeHGlobal(pointer);
            }

            _allocatedStrings.Clear();
            Marshal.FreeHGlobal(_instancePointer);
            Marshal.FreeHGlobal(_cancelHandle);
            if (_selfHandle.IsAllocated)
            {
                _selfHandle.Free();
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void DnsServiceRegisterComplete(
            uint status,
            IntPtr queryContext,
            IntPtr serviceInstance);

        [StructLayout(LayoutKind.Sequential)]
        private struct DnsServiceRegisterRequest
        {
            public uint Version;
            public uint InterfaceIndex;
            public IntPtr ServiceInstance;
            public IntPtr CompletionCallback;
            public IntPtr QueryContext;
            public IntPtr Credentials;

            [MarshalAs(UnmanagedType.Bool)]
            public bool UnicastEnabled;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DnsServiceInstance
        {
            public IntPtr InstanceName;
            public IntPtr HostName;
            public IntPtr Ip4Address;
            public IntPtr Ip6Address;
            public ushort Port;
            public ushort Priority;
            public ushort Weight;
            public uint PropertyCount;
            public IntPtr Keys;
            public IntPtr Values;
            public uint InterfaceIndex;
        }

        [DllImport("dnsapi.dll", ExactSpelling = true)]
        private static extern uint DnsServiceRegister(
            ref DnsServiceRegisterRequest request,
            IntPtr cancelHandle);

        [DllImport("dnsapi.dll", ExactSpelling = true)]
        private static extern uint DnsServiceRegisterCancel(IntPtr cancelHandle);

        [DllImport("dnsapi.dll", ExactSpelling = true)]
        private static extern uint DnsServiceDeRegister(
            ref DnsServiceRegisterRequest request,
            IntPtr cancelHandle);

        [DllImport("dnsapi.dll", ExactSpelling = true)]
        private static extern void DnsServiceFreeInstance(IntPtr serviceInstance);
    }
}
