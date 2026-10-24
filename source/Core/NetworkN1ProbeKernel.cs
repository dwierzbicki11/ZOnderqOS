#if ZONDERQ_NETWORK_N1_PROBE
using System;
using System.Threading;
using Cosmos.Kernel.Core.IO;
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
            Serial.WriteString(line + "\n");
            Console.WriteLine(line);
        }

        protected override void BeforeRun()
        {
            Log("[NET3094-N1] BOOT");
            if (!NetworkManager.IsEnabled || !NetworkManager.IsInitialized)
            {
                Log("[NET3094-N1][FAIL] Network manager not initialized");
                return;
            }

            int count = NetworkManager.DeviceCount;
            Log("[NET3094-N1] discovered=" + count);
            if (count <= 0 || count > 8)
            {
                Log("[NET3094-N1][FAIL] No valid NIC registration");
                return;
            }

            int readyLinks = 0;
            for (int i = 0; i < count; i++)
            {
                NetworkAdapter adapter = NetworkManager.GetAdapter(i);
                if (!adapter.IsValid)
                {
                    Log("[NET3094-N1][FAIL] Invalid device index=" + i);
                    return;
                }

                string name = adapter.Name ?? "unknown";
                string mac = adapter.MacAddress?.ToString() ?? "unknown";
                bool ready = adapter.Ready;
                bool link = adapter.LinkUp;
                Log("[NET3094-N1] cpu=bsp adapter=" + i + " name=" + name +
                    " mac=" + mac + " ready=" + (ready ? "yes" : "no") +
                    " link=" + (link ? "up" : "down"));
                if (ready && link)
                    readyLinks++;
            }

            if (readyLinks == 0)
            {
                Log("[NET3094-N1][FAIL] NIC registered but link not ready");
                return;
            }

            Log("[NET3094-N1] PASS READY_LINK=" + readyLinks);
            Log("[NET3094-N1] DHCP=NOT_ATTEMPTED IP=UNASSIGNED");
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
