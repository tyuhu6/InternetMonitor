using System;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace InternetMonitor;

/// <summary>
/// 读取所有已启用网卡的累计收/发字节数（含 IPv4 与 IPv6 流量）。
/// 优先使用 GetIfEntry2（MIB_IF_ROW2 的 InOctets/OutOctets 为整卡总量），
/// 若该环境不可用则回退到 .NET 的 IPv4 统计。
/// </summary>
internal static class NetworkStatsReader
{
    private const uint IF_OPER_STATUS_UP = 1;
    private const int MIB_IF_ROW2_SIZE = 1352; // x64 下 MIB_IF_ROW2 的字节数

    private static readonly bool UseNativeIfEntry = Marshal.SizeOf<MIB_IF_ROW2>() == MIB_IF_ROW2_SIZE;

    /// <summary>返回所有活动网卡的 (接收字节总数, 发送字节总数)。</summary>
    public static (ulong Received, ulong Sent) ReadTotals()
    {
        ulong rx = 0;
        ulong tx = 0;

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;

            var type = nic.NetworkInterfaceType;
            if (type == NetworkInterfaceType.Loopback || type == NetworkInterfaceType.Tunnel)
                continue;

            int index = GetInterfaceIndex(nic);
            if (index < 0)
                continue;

            if (UseNativeIfEntry && TryReadIfEntry((uint)index, out ulong nativeRx, out ulong nativeTx))
            {
                rx += nativeRx;
                tx += nativeTx;
                continue;
            }

            try
            {
                var stats = nic.GetIPv4Statistics();
                if (stats.BytesReceived > 0)
                    rx += (ulong)stats.BytesReceived;
                if (stats.BytesSent > 0)
                    tx += (ulong)stats.BytesSent;
            }
            catch (NetworkInformationException)
            {
                // 该网卡未绑定 IPv4，忽略
            }
        }

        return (rx, tx);
    }

    private static int GetInterfaceIndex(NetworkInterface nic)
    {
        var properties = nic.GetIPProperties();
        try
        {
            var ipv4 = properties.GetIPv4Properties();
            if (ipv4 != null)
                return ipv4.Index;
        }
        catch (NetworkInformationException)
        {
            // 无 IPv4 绑定
        }

        try
        {
            var ipv6 = properties.GetIPv6Properties();
            if (ipv6 != null)
                return ipv6.Index;
        }
        catch (NetworkInformationException)
        {
            // 无 IPv6 绑定
        }

        return -1;
    }

    private static bool TryReadIfEntry(uint index, out ulong rx, out ulong tx)
    {
        rx = 0;
        tx = 0;

        var row = default(MIB_IF_ROW2);
        row.InterfaceIndex = index;

        if (GetIfEntry2(ref row) != 0 || row.OperStatus != IF_OPER_STATUS_UP)
            return false;

        rx = row.InOctets;
        tx = row.OutOctets;
        return true;
    }

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetIfEntry2(ref MIB_IF_ROW2 row);

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct MIB_IF_ROW2
    {
        public ulong InterfaceLuid;
        public uint InterfaceIndex;
        public Guid InterfaceGuid;
        public fixed char Alias[257];
        public fixed char Description[257];
        public uint PhysicalAddressLength;
        public fixed byte PhysicalAddress[32];
        public fixed byte PermanentPhysicalAddress[32];
        public uint Mtu;
        public uint Type;
        public uint TunnelType;
        public uint MediaType;
        public uint PhysicalMediumType;
        public uint AccessType;
        public uint DirectionType;
        public fixed byte InterfaceAndOperStatusFlags[8];
        public uint OperStatus;
        public uint AdminStatus;
        public uint MediaConnectState;
        public Guid NetworkGuid;
        public uint ConnectionType;
        public ulong TransmitLinkSpeed;
        public ulong ReceiveLinkSpeed;
        public ulong InOctets;
        public ulong InUcastPkts;
        public ulong InNUcastPkts;
        public ulong InDiscards;
        public ulong InErrors;
        public ulong InUnknownProtos;
        public ulong InUcastOctets;
        public ulong InMulticastOctets;
        public ulong InBroadcastOctets;
        public ulong OutOctets;
        public ulong OutUcastPkts;
        public ulong OutNUcastPkts;
        public ulong OutDiscards;
        public ulong OutErrors;
        public ulong OutUcastOctets;
        public ulong OutMulticastOctets;
        public ulong OutBroadcastOctets;
        public ulong OutQLen;
    }
}
