#if ZONDERQ_NETWORK_N1_PROBE
using System;
using System.Threading;
using DiagnosticLog = Cosmos.Kernel.System.Diagnostics.Log;
using Cosmos.Kernel.System.Network;
using Sys = Cosmos.Kernel.System;

namespace ZonderqOS
{
    /// <summary>
    /// Isolated x64 network probe kernel. Never mounts storage, initializes
    /// user accounts or launches the GUI. Runs only in opted-in QEMU profile.
    /// </summary>
    public sealed class NetworkN1ProbeKernel : Sys.Kernel
    {
        private static void Log(string line)
        {
            DiagnosticLog.WriteString(line + "\n");
        }

        protected override void BeforeRun()
        {
            Log("[NET3095-N1] BOOT");
            if (!NetworkManager.IsEnabled || !NetworkManager.IsInitialized)
            {
                Log("[NET3095-N1][FAIL] Network manager not initialized");
                return;
            }

            int count = NetworkManager.DeviceCount;
            Log("[NET3095-N1] discovered=" + count);
            if (count <= 0 || count > 8)
            {
                Log("[NET3095-N1][FAIL] No valid NIC registration");
                return;
            }

            int readyLinks = 0;
            for (int i = 0; i < count; i++)
            {
                NetworkAdapter adapter = NetworkManager.GetAdapter(i);
                if (!adapter.IsValid)
                {
                    Log("[NET3095-N1][FAIL] Invalid device index=" + i);
                    return;
                }

                string name = adapter.Name ?? "unknown";
                string mac = adapter.MacAddress?.ToString() ?? "unknown";
                bool ready = adapter.Ready;
                bool link = adapter.LinkUp;
                Log("[NET3095-N1] cpu=bsp adapter=" + i + " name=" + name +
                    " mac=" + mac + " ready=" + (ready ? "yes" : "no") +
                    " link=" + (link ? "up" : "down"));
                if (name == "e1000e" && ready && link)
                    readyLinks++;
            }

            if (readyLinks == 0)
            {
                Log("[NET3095-N1][FAIL] NIC registered but link not ready");
                return;
            }

            Log("[NET3095-N1] PASS READY_LINK=" + readyLinks);
            // Only an isolated QEMU build reaches this path. This is a raw
            // Ethernet test, not a DHCP request or an IP configuration.
            NetworkAdapter primary = NetworkManager.Primary;
            if (!primary.IsValid || primary.Name != "e1000e" ||
                !primary.Ready || !primary.LinkUp)
            {
                Log("[NET3095-N1][FAIL] Primary E1000E not ready");
                return;
            }

            string sourceMac = primary.MacAddress?.ToString() ?? "";
            byte[] frame = new byte[60]; // Minimum Ethernet frame excluding FCS.
            for (int i = 0; i < 6; i++)
                frame[i] = 0xFF; // Broadcast destination.
            if (!TryFillSourceMac(sourceMac, frame))
            {
                Log("[NET3095-N1][FAIL] Invalid source MAC");
                return;
            }

            frame[12] = 0x88;
            frame[13] = 0xB5; // Local experimental EtherType.
            byte[] payload = { 0x5A, 0x4F, 0x4E, 0x44, 0x45, 0x52, 0x51,
                               0x33, 0x30, 0x39, 0x35, 0x54, 0x58 }; // ZONDERQ3095TX
            for (int i = 0; i < payload.Length; i++)
                frame[14 + i] = payload[i];

            bool queued = NetworkManager.Send(frame, frame.Length);
            Log("[NET3095-N1] TX_QUEUED=" + (queued ? "yes" : "no"));
            if (!queued)
            {
                Log("[NET3095-N1][FAIL] E1000E TX queue rejected the frame");
                return;
            }
            Log("[NET3095-N1] DHCP=NOT_ATTEMPTED IP=UNASSIGNED");
        }

        private static int HexDigit(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        private static bool TryFillSourceMac(string mac, byte[] frame)
        {
            if (mac.Length != 17)
                return false;
            for (int i = 0; i < 6; i++)
            {
                int pos = i * 3;
                if (i > 0 && mac[pos - 1] != ':')
                    return false;
                int hi = HexDigit(mac[pos]);
                int lo = HexDigit(mac[pos + 1]);
                if (hi < 0 || lo < 0)
                    return false;
                frame[6 + i] = (byte)((hi << 4) | lo);
            }
            return (frame[6] & 1) == 0 &&
                   (frame[6] | frame[7] | frame[8] |
                    frame[9] | frame[10] | frame[11]) != 0;
        }

        protected override void Run()
        {
            // Allow serial evidence and background driver-kit work to remain
            // observable; do not touch the host filesystem.
            Thread.Sleep(1000);
        }
    }
}
#endif
