using System.Runtime.InteropServices;
using System.Text;
using PresenceTracker.Domain;

namespace PresenceTracker.NetworkMonitoring;

public sealed record WlanNetworkTransition(DateTimeOffset OccurredAt, string InterfaceId,
    string InterfaceName, string? Ssid, NetworkEventType Type);

public sealed class WlanMonitor : IDisposable
{
    private const uint NotificationSourceNone = 0;
    private const uint NotificationSourceAcm = 0x00000008;
    private const uint AcmConnectionComplete = 10;
    private const uint AcmDisconnected = 21;
    private const int CurrentConnectionOpcode = 7;
    private readonly object gate = new();
    private readonly Dictionary<Guid, string> connectedSsids = [];
    private readonly Dictionary<Guid, (DateOnly Date, string Ssid)> presenceObserved = [];
    private readonly WlanNotificationCallback callback;
    private IntPtr clientHandle;
    private bool disposed;

    public event Action<WlanNetworkTransition>? NetworkChanged;

    public WlanMonitor() => callback = OnNotification;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (clientHandle != IntPtr.Zero) return;
        var result = Native.WlanOpenHandle(2, IntPtr.Zero, out _, out clientHandle);
        ThrowIfError(result, "WlanOpenHandle");
        result = Native.WlanRegisterNotification(clientHandle, NotificationSourceAcm, true,
            callback, IntPtr.Zero, IntPtr.Zero, out _);
        if (result != 0)
        {
            Native.WlanCloseHandle(clientHandle, IntPtr.Zero);
            clientHandle = IntPtr.Zero;
            ThrowIfError(result, "WlanRegisterNotification");
        }
        CaptureInitialState();
    }

    public void PrepareForSuspend()
    {
        lock (gate)
            connectedSsids.Clear();
    }

    public int RecordCurrentConnectionsForNetworks(IEnumerable<string> ssids, bool force = false)
        => CaptureCurrentConnections(ssids, force, emitTransition: true);

    public int MarkCurrentConnectionsForNetworks(IEnumerable<string> ssids)
        => CaptureCurrentConnections(ssids, force: true, emitTransition: false);

    private int CaptureCurrentConnections(IEnumerable<string> ssids, bool force, bool emitTransition)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (clientHandle == IntPtr.Zero)
            return 0;

        var configured = ssids.Where(ssid => !string.IsNullOrWhiteSpace(ssid))
            .Select(ssid => ssid.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (configured.Count == 0)
            return 0;

        var observedAt = DateTimeOffset.Now;
        var localDate = DateOnly.FromDateTime(observedAt.LocalDateTime);
        var recorded = 0;
        foreach (var item in GetInterfaces())
        {
            var current = QuerySsid(item.Guid);
            if (current is null || !configured.Contains(current))
                continue;

            lock (gate)
            {
                if (!force && presenceObserved.TryGetValue(item.Guid, out var previous) &&
                    previous.Date == localDate && string.Equals(previous.Ssid, current, StringComparison.OrdinalIgnoreCase))
                    continue;
                connectedSsids[item.Guid] = current;
                presenceObserved[item.Guid] = (localDate, current);
            }
            if (emitTransition)
                NetworkChanged?.Invoke(new WlanNetworkTransition(observedAt,
                    item.Guid.ToString("D"), item.Description ?? "Wi-Fi", current, NetworkEventType.Connected));
            recorded++;
        }

        return recorded;
    }

    private void CaptureInitialState()
    {
        foreach (var item in GetInterfaces())
        {
            var current = QuerySsid(item.Guid);
            if (current is not null)
            {
                lock (gate) connectedSsids[item.Guid] = current;
            }
        }
    }

    private void OnNotification(IntPtr data, IntPtr context)
    {
        try
        {
            var notification = Marshal.PtrToStructure<NotificationData>(data);
            if (notification.Source != NotificationSourceAcm ||
                notification.Code is not (AcmConnectionComplete or AcmDisconnected))
                return;

            var interfaceInfo = GetInterfaces().FirstOrDefault(i => i.Guid == notification.InterfaceGuid);
            var interfaceName = interfaceInfo.Description ?? "Wi-Fi";
            var fromPayload = ReadNotificationSsid(notification.Data);
            if (notification.Code == AcmConnectionComplete)
            {
                var connection = Marshal.PtrToStructure<ConnectionNotificationData>(notification.Data);
                if (connection.ReasonCode != 0 || string.IsNullOrWhiteSpace(fromPayload))
                    return;
                string? previous;
                lock (gate)
                {
                    connectedSsids.TryGetValue(notification.InterfaceGuid, out previous);
                    connectedSsids[notification.InterfaceGuid] = fromPayload;
                }
                if (!string.Equals(previous, fromPayload, StringComparison.Ordinal))
                {
                    var observedAt = DateTimeOffset.Now;
                    lock (gate)
                        presenceObserved[notification.InterfaceGuid] =
                            (DateOnly.FromDateTime(observedAt.LocalDateTime), fromPayload);
                    NetworkChanged?.Invoke(new WlanNetworkTransition(observedAt,
                        notification.InterfaceGuid.ToString("D"), interfaceName, fromPayload, NetworkEventType.Connected));
                }
                return;
            }

            var current = QuerySsid(notification.InterfaceGuid);
            string? disconnected;
            lock (gate)
            {
                connectedSsids.TryGetValue(notification.InterfaceGuid, out var previous);
                disconnected = fromPayload ?? previous;
                if (current is null || string.Equals(current, disconnected, StringComparison.Ordinal))
                    connectedSsids.Remove(notification.InterfaceGuid);
                else
                    connectedSsids[notification.InterfaceGuid] = current;
            }
            if (disconnected is not null)
                NetworkChanged?.Invoke(new WlanNetworkTransition(DateTimeOffset.Now,
                    notification.InterfaceGuid.ToString("D"), interfaceName, disconnected, NetworkEventType.Disconnected));
            if (current is not null && !string.Equals(current, disconnected, StringComparison.Ordinal))
            {
                var observedAt = DateTimeOffset.Now;
                lock (gate)
                    presenceObserved[notification.InterfaceGuid] = (DateOnly.FromDateTime(observedAt.LocalDateTime), current);
                NetworkChanged?.Invoke(new WlanNetworkTransition(observedAt,
                    notification.InterfaceGuid.ToString("D"), interfaceName, current, NetworkEventType.Connected));
            }
        }
        catch
        {
            // Native callbacks must not allow managed exceptions to escape into wlanapi.dll.
        }
    }

    private IReadOnlyList<WlanInterface> GetInterfaces()
    {
        var result = Native.WlanEnumInterfaces(clientHandle, IntPtr.Zero, out var list);
        ThrowIfError(result, "WlanEnumInterfaces");
        try
        {
            var header = Marshal.PtrToStructure<InterfaceListHeader>(list);
            var first = IntPtr.Add(list, Marshal.SizeOf<InterfaceListHeader>());
            var size = Marshal.SizeOf<WlanInterface>();
            var interfaces = new List<WlanInterface>((int)header.Count);
            for (var index = 0; index < header.Count; index++)
                interfaces.Add(Marshal.PtrToStructure<WlanInterface>(IntPtr.Add(first, checked((int)index * size))));
            return interfaces;
        }
        finally
        {
            Native.WlanFreeMemory(list);
        }
    }

    private static string? ReadNotificationSsid(IntPtr data)
    {
        if (data == IntPtr.Zero) return null;
        var connection = Marshal.PtrToStructure<ConnectionNotificationData>(data);
        return DecodeSsid(connection.Ssid);
    }

    private string? QuerySsid(Guid interfaceGuid)
    {
        var result = Native.WlanQueryInterface(clientHandle, ref interfaceGuid, CurrentConnectionOpcode,
            IntPtr.Zero, out _, out var data, out _);
        if (result != 0) return null;
        try
        {
            var connection = Marshal.PtrToStructure<ConnectionAttributes>(data);
            if (connection.State != 1)
                return null;
            return DecodeSsid(connection.Association.Ssid);
        }
        finally
        {
            Native.WlanFreeMemory(data);
        }
    }

    private static string? DecodeSsid(Dot11Ssid ssid)
    {
        if (ssid.Length is 0 or > 32 || ssid.Bytes is null)
            return null;
        return Encoding.UTF8.GetString(ssid.Bytes, 0, (int)ssid.Length);
    }

    private static void ThrowIfError(uint result, string operation)
    {
        if (result != 0)
            throw new InvalidOperationException($"{operation} falhou com código Win32 {result}.");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (clientHandle != IntPtr.Zero)
        {
            Native.WlanRegisterNotification(clientHandle, NotificationSourceNone, true, null,
                IntPtr.Zero, IntPtr.Zero, out _);
            Native.WlanCloseHandle(clientHandle, IntPtr.Zero);
            clientHandle = IntPtr.Zero;
        }
        GC.SuppressFinalize(this);
    }

    private delegate void WlanNotificationCallback(IntPtr notificationData, IntPtr context);

    [StructLayout(LayoutKind.Sequential)]
    private struct NotificationData
    {
        public uint Source;
        public uint Code;
        public Guid InterfaceGuid;
        public uint DataSize;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InterfaceListHeader
    {
        public uint Count;
        public uint Index;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanInterface
    {
        public Guid Guid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Description;
        public int State;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Dot11Ssid
    {
        public uint Length;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] Bytes;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ConnectionNotificationData
    {
        public int Mode;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string ProfileName;
        public Dot11Ssid Ssid;
        public int BssType;
        public int SecurityEnabled;
        public uint ReasonCode;
        public uint Flags;
        public IntPtr ProfileXml;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AssociationAttributes
    {
        public Dot11Ssid Ssid;
        public int BssType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] Bssid;
        public int PhyType;
        public uint PhyIndex;
        public uint SignalQuality;
        public uint RxRate;
        public uint TxRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int SecurityEnabled;
        public int OneXEnabled;
        public int AuthAlgorithm;
        public int CipherAlgorithm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ConnectionAttributes
    {
        public int State;
        public int Mode;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string ProfileName;
        public AssociationAttributes Association;
        public SecurityAttributes Security;
    }

    private static class Native
    {
        [DllImport("wlanapi.dll")]
        public static extern uint WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr clientHandle);
        [DllImport("wlanapi.dll")]
        public static extern uint WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);
        [DllImport("wlanapi.dll")]
        public static extern uint WlanRegisterNotification(IntPtr clientHandle, uint source, [MarshalAs(UnmanagedType.Bool)] bool ignoreDuplicate,
            WlanNotificationCallback? callback, IntPtr callbackContext, IntPtr reserved, out uint previousSource);
        [DllImport("wlanapi.dll")]
        public static extern uint WlanEnumInterfaces(IntPtr clientHandle, IntPtr reserved, out IntPtr interfaceList);
        [DllImport("wlanapi.dll")]
        public static extern uint WlanQueryInterface(IntPtr clientHandle, ref Guid interfaceGuid, int opcode, IntPtr reserved,
            out uint dataSize, out IntPtr data, out int valueType);
        [DllImport("wlanapi.dll")]
        public static extern void WlanFreeMemory(IntPtr memory);
    }
}





