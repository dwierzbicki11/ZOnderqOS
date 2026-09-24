using System;
using System.Collections.Generic;
using System.Threading;
using Cosmos.Kernel.System.Diagnostics;
using Sys = Cosmos.Kernel.System;
using ZonderqOS.Hardware;
using ZonderqOS.Platform.X64;

namespace ZonderqOS
{
    /// <summary>D2.3 runtime proof: verified D2.2 capability reads followed by bounded AHCI reset.</summary>
    public sealed class DriverD2EntryKernel : Sys.Kernel
    {
        private const uint AhciGhcHr = 1u << 0;
        private bool completed;

        protected override void BeforeRun()
        {
            Log.WriteString("[DRIVER-D2.3] starting storage capability probe and AHCI reset\n");
            try
            {
                var config = new PciConfigIoAccessor();
                var source = new PciConfigDiscoverySource(config);
                var snapshots = new List<PciFunctionSnapshot>(source.Discover());
                var discovery = new PciDiscoveryService(new PciConfigDiscoverySource(config));
                List<DeviceDescriptor> devices = discovery.DiscoverDevices();
                var ahci = new AhciControllerDriver();
                var nvme = new NvmeControllerDriver();
                int ahciBound = 0;
                int nvmeBound = 0;

                for (int i = 0; i < devices.Count; i++)
                {
                    if (ahci.Bind(devices[i])) ahciBound++;
                    if (nvme.Bind(devices[i])) nvmeBound++;
                }
                if (ahciBound < 1) throw new InvalidOperationException("no AHCI controller bound");
                if (nvmeBound < 1) throw new InvalidOperationException("no NVMe controller bound");

                int ahciProbed = 0;
                int nvmeProbed = 0;
                PciMemoryBar ahciBar = default;
                for (int i = 0; i < snapshots.Count; i++)
                {
                    PciFunctionSnapshot function = snapshots[i];
                    PciMemoryBar bar;
                    if (StorageControllerBars.TryReadAhciAbar(config, function, out bar))
                    {
                        uint cap = PhysicalMmioReader.Read32(bar.Address + 0x00UL);
                        uint pi = PhysicalMmioReader.Read32(bar.Address + 0x0CUL);
                        if (cap == 0xFFFFFFFFu || pi == 0xFFFFFFFFu)
                            throw new InvalidOperationException("AHCI capability MMIO returned all-ones");
                        ahciBar = bar;
                        ahciProbed++;
                        Log.WriteString("[DRIVER-D2.2] AHCI CAP/PI read\n");
                    }
                    if (StorageControllerBars.TryReadNvmeBar0(config, function, out bar))
                    {
                        ulong cap = PhysicalMmioReader.Read64(bar.Address + 0x00UL);
                        uint vs = PhysicalMmioReader.Read32(bar.Address + 0x08UL);
                        uint csts = PhysicalMmioReader.Read32(bar.Address + 0x1CUL);
                        if (cap == ulong.MaxValue || vs == 0xFFFFFFFFu || csts == 0xFFFFFFFFu)
                            throw new InvalidOperationException("NVMe capability MMIO returned all-ones");
                        if (vs == 0u)
                            throw new InvalidOperationException("NVMe version register is zero");
                        nvmeProbed++;
                        Log.WriteString("[DRIVER-D2.2] NVME CAP/VS/CSTS read\n");
                    }
                }

                if (ahciProbed != 1) throw new InvalidOperationException("expected exactly one AHCI controller capability-probed");
                if (nvmeProbed < 1) throw new InvalidOperationException("no NVMe controller capability-probed");
                Log.WriteString("[DRIVER-D2.2] PASS read-only capability MMIO\n");

                // AHCI 1.x section 10.4.3: set GHC.HR and wait for hardware to clear it.
                // Keep this first D2.3 checkpoint deliberately controller-global: no port
                // command engine, DMA structures, interrupts, or storage commands yet.
                ulong ghcAddress = checked(ahciBar.Address + 0x04UL);
                uint ghc = PhysicalMmioReader.Read32(ghcAddress);
                if (ghc == 0xFFFFFFFFu)
                    throw new InvalidOperationException("AHCI GHC returned all-ones before reset");
                PhysicalMmioWriter.Write32(ghcAddress, ghc | AhciGhcHr);

                bool resetComplete = false;
                for (int spin = 0; spin < 1_000_000; spin++)
                {
                    uint current = PhysicalMmioReader.Read32(ghcAddress);
                    if (current == 0xFFFFFFFFu)
                        throw new InvalidOperationException("AHCI GHC returned all-ones during reset");
                    if ((current & AhciGhcHr) == 0)
                    {
                        resetComplete = true;
                        break;
                    }
                    Thread.SpinWait(32);
                }
                if (!resetComplete)
                    throw new TimeoutException("AHCI HBA reset did not clear GHC.HR");

                uint capAfterReset = PhysicalMmioReader.Read32(checked(ahciBar.Address + 0x00UL));
                if (capAfterReset == 0xFFFFFFFFu)
                    throw new InvalidOperationException("AHCI CAP invalid after reset");
                Log.WriteString("[DRIVER-D2.3] AHCI HBA reset complete\n");
                Log.WriteString("[DRIVER-D2.3] PASS AHCI reset checkpoint\n");
            }
            catch (Exception ex)
            {
                Log.WriteString("[DRIVER-D2.3] FAIL ");
                Log.WriteString(ex.GetType().Name);
                Log.WriteString(": ");
                Log.WriteString(ex.Message);
                Log.WriteString("\n");
            }
            completed = true;
        }

        protected override void Run()
        {
            if (completed) Thread.Sleep(1000);
        }
    }
}
