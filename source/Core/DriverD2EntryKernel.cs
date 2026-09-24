using System;
using System.Collections.Generic;
using System.Threading;
using Cosmos.Kernel.System.Diagnostics;
using Sys = Cosmos.Kernel.System;
using ZonderqOS.Hardware;
using ZonderqOS.Platform.X64;

namespace ZonderqOS
{
    /// <summary>D2.3 runtime proof: verified D2.2 reads, AHCI reset, and real NVMe admin-queue initialization.</summary>
    public sealed class DriverD2EntryKernel : Sys.Kernel
    {
        private const uint AhciGhcHr = 1u << 0;
        private const uint NvmeCcEn = 1u << 0;
        private const uint NvmeCstsRdy = 1u << 0;
        private const uint NvmeCstsCfs = 1u << 1;
        private const uint NvmeAdminQueueEntries = 64;
        private bool completed;

        protected override void BeforeRun()
        {
            Log.WriteString("[DRIVER-D2.3] starting storage capability probe and controller reset/init\n");
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
                PciMemoryBar nvmeBar = default;
                ulong nvmeCap = 0;
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
                        nvmeBar = bar;
                        nvmeCap = cap;
                        nvmeProbed++;
                        Log.WriteString("[DRIVER-D2.2] NVME CAP/VS/CSTS read\n");
                    }
                }

                if (ahciProbed != 1) throw new InvalidOperationException("expected exactly one AHCI controller capability-probed");
                if (nvmeProbed != 1) throw new InvalidOperationException("expected exactly one NVMe controller capability-probed");
                Log.WriteString("[DRIVER-D2.2] PASS read-only capability MMIO\n");

                ulong ghcAddress = checked(ahciBar.Address + 0x04UL);
                Log.WriteString("[DRIVER-D2.3] AHCI reset: reading GHC before HR\n");
                uint ghc = PhysicalMmioReader.Read32(ghcAddress);
                if (ghc == 0xFFFFFFFFu)
                    throw new InvalidOperationException("AHCI GHC returned all-ones before reset");
                Log.WriteString("[DRIVER-D2.3] AHCI reset: writing GHC.HR\n");
                PhysicalMmioWriter.Write32(ghcAddress, ghc | AhciGhcHr);
                Log.WriteString("[DRIVER-D2.3] AHCI reset: GHC.HR write returned\n");

                bool resetComplete = false;
                for (int spin = 0; spin < 1_000_000; spin++)
                {
                    uint current = PhysicalMmioReader.Read32(ghcAddress);
                    if (current == 0xFFFFFFFFu)
                        throw new InvalidOperationException("AHCI GHC returned all-ones during reset");
                    if ((current & AhciGhcHr) == 0) { resetComplete = true; break; }
                    Thread.SpinWait(32);
                }
                if (!resetComplete) throw new TimeoutException("AHCI HBA reset did not clear GHC.HR");
                uint capAfterReset = PhysicalMmioReader.Read32(checked(ahciBar.Address + 0x00UL));
                if (capAfterReset == 0xFFFFFFFFu) throw new InvalidOperationException("AHCI CAP invalid after reset");
                Log.WriteString("[DRIVER-D2.3] AHCI HBA reset complete\n");

                ulong nvmeCcAddress = checked(nvmeBar.Address + 0x14UL);
                ulong nvmeCstsAddress = checked(nvmeBar.Address + 0x1CUL);
                uint cc = PhysicalMmioReader.Read32(nvmeCcAddress);
                uint cstsBefore = PhysicalMmioReader.Read32(nvmeCstsAddress);
                if (cc == 0xFFFFFFFFu || cstsBefore == 0xFFFFFFFFu)
                    throw new InvalidOperationException("NVMe CC/CSTS returned all-ones before disable");
                Log.WriteString("[DRIVER-D2.3] NVME disable: clearing CC.EN\n");
                PhysicalMmioWriter.Write32(nvmeCcAddress, cc & ~NvmeCcEn);

                uint capTimeoutUnits = (uint)((nvmeCap >> 24) & 0xFFUL);
                int pollLimit = capTimeoutUnits == 0 ? 100_000 : checked((int)capTimeoutUnits * 100_000);
                bool nvmeDisabled = false;
                for (int spin = 0; spin < pollLimit; spin++)
                {
                    uint csts = PhysicalMmioReader.Read32(nvmeCstsAddress);
                    if (csts == 0xFFFFFFFFu) throw new InvalidOperationException("NVMe CSTS returned all-ones during disable");
                    if ((csts & NvmeCstsRdy) == 0) { nvmeDisabled = true; break; }
                    Thread.SpinWait(32);
                }
                if (!nvmeDisabled) throw new TimeoutException("NVMe controller did not clear CSTS.RDY after CC.EN=0");
                Log.WriteString("[DRIVER-D2.3] NVME disabled CSTS.RDY=0\n");

                uint mqes = (uint)(nvmeCap & 0xFFFFUL) + 1u;
                uint mpsMin = (uint)((nvmeCap >> 48) & 0xFUL);
                if (mqes < NvmeAdminQueueEntries)
                    throw new InvalidOperationException("NVMe CAP.MQES cannot support the 64-entry admin queues");
                if (mpsMin != 0)
                    throw new InvalidOperationException("NVMe controller requires pages larger than 4 KiB");

                DmaPageAllocator.InitializeBarrier();
                DmaPageAllocator.Buffer adminSq = DmaPageAllocator.AllocateZeroed(1);
                DmaPageAllocator.Buffer adminCq = DmaPageAllocator.AllocateZeroed(1);
                if (adminSq.PhysicalAddress == adminCq.PhysicalAddress)
                    throw new InvalidOperationException("NVMe admin SQ/CQ DMA pages alias");
                DmaPageAllocator.Barrier();
                Log.WriteString("[DRIVER-D2.3] NVME admin SQ/CQ DMA pages allocated\n");

                uint queueSizeZeroBased = NvmeAdminQueueEntries - 1u;
                uint aqa = (queueSizeZeroBased << 16) | queueSizeZeroBased;
                ulong aqaAddress = checked(nvmeBar.Address + 0x24UL);
                ulong asqAddress = checked(nvmeBar.Address + 0x28UL);
                ulong acqAddress = checked(nvmeBar.Address + 0x30UL);
                PhysicalMmioWriter.Write32(aqaAddress, aqa);
                PhysicalMmioWriter.Write64(asqAddress, adminSq.PhysicalAddress);
                PhysicalMmioWriter.Write64(acqAddress, adminCq.PhysicalAddress);
                if (PhysicalMmioReader.Read32(aqaAddress) != aqa ||
                    PhysicalMmioReader.Read64(asqAddress) != adminSq.PhysicalAddress ||
                    PhysicalMmioReader.Read64(acqAddress) != adminCq.PhysicalAddress)
                    throw new InvalidOperationException("NVMe admin queue register readback mismatch");
                Log.WriteString("[DRIVER-D2.3] NVME AQA/ASQ/ACQ programmed and verified\n");

                uint enableCc = NvmeCcEn | (6u << 16) | (4u << 20);
                PhysicalMmioWriter.Write32(nvmeCcAddress, enableCc);
                bool nvmeReady = false;
                for (int spin = 0; spin < pollLimit; spin++)
                {
                    uint csts = PhysicalMmioReader.Read32(nvmeCstsAddress);
                    if (csts == 0xFFFFFFFFu) throw new InvalidOperationException("NVMe CSTS returned all-ones during enable");
                    if ((csts & NvmeCstsCfs) != 0) throw new InvalidOperationException("NVMe controller reported CSTS.CFS during enable");
                    if ((csts & NvmeCstsRdy) != 0) { nvmeReady = true; break; }
                    Thread.SpinWait(32);
                }
                if (!nvmeReady) throw new TimeoutException("NVMe controller did not set CSTS.RDY after admin queue setup and CC.EN=1");
                Log.WriteString("[DRIVER-D2.3] NVME enabled CSTS.RDY=1 with real admin queues\n");
                Log.WriteString("[DRIVER-D2.3] PASS AHCI reset + NVMe admin queue init checkpoint\n");
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
