using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.ARM64.Cpu;

namespace ZonderqOS
{
    /// <summary>
    /// Minimal Raspberry Pi 4 VL805 xHCI stack for a boot-protocol USB keyboard.
    ///
    /// Design constraints for the physical Pi 4 bring-up:
    /// - polling only: xHCI MSI/INTx is not used yet;
    /// - non-coherent PCIe DMA: every shared object gets explicit cache maintenance;
    /// - DMA must stay below 3 GiB, matching PFTF's XHC0 _DMA window;
    /// - the Pi 4 main USB2 sockets sit behind VL805's built-in VIA USB2 hub,
    ///   therefore one hub tier is intentionally supported;
    /// - only control EP0 and one HID Interrupt-IN endpoint are configured.
    ///
    /// This does not replace Cosmos' generic keyboard stack. CosmosEnableKeyboard
    /// stays false on ARM64 so QEMU virtio input is never probed on Raspberry Pi.
    /// </summary>
    internal static unsafe class Rpi4XhciKeyboard
    {
        private const ulong PcieRegPhysicalBase = 0xFD500000UL;
        private const ulong PcieCpuMmioWindow = 0x600000000UL;
        private const ulong PcieBusMmioBase = 0xF8000000UL;
        private const ulong PcieBusMmioLength = 0x04000000UL;
        private const ulong PcieExtCfgDataOffset = 0x8000UL;
        private const ulong PcieExtCfgIndexOffset = 0x9000UL;

        private const ushort PciCommandMemorySpace = 0x0002;
        private const ushort PciCommandBusMaster = 0x0004;

        private const int CommandRingTrbs = 256;
        private const int EventRingTrbs = 256;
        private const int TransferRingTrbs = 256;
        private const nuint DmaArenaSize = 384 * 1024;
        private const int DescriptorBufferSize = 1024;

        // Capability register offsets.
        private const ulong HcsParams1Offset = 0x04;
        private const ulong HcsParams2Offset = 0x08;
        private const ulong HccParams1Offset = 0x10;
        private const ulong DoorbellOffset = 0x14;
        private const ulong RuntimeOffset = 0x18;

        // Operational register offsets from CAPLENGTH.
        private const ulong UsbCmdOffset = 0x00;
        private const ulong UsbStsOffset = 0x04;
        private const ulong PageSizeOffset = 0x08;
        private const ulong CrcrOffset = 0x18;
        private const ulong DcbaapOffset = 0x30;
        private const ulong ConfigOffset = 0x38;
        private const ulong PortBaseOffset = 0x400;
        private const ulong PortStride = 0x10;

        // Runtime interrupter 0 register offsets.
        private const ulong Ir0BaseOffset = 0x20;
        private const ulong ImanOffset = 0x00;
        private const ulong ImodOffset = 0x04;
        private const ulong ErstszOffset = 0x08;
        private const ulong ErstbaOffset = 0x10;
        private const ulong ErdpOffset = 0x18;

        // USBCMD / USBSTS.
        private const uint CmdRun = 1U << 0;
        private const uint CmdReset = 1U << 1;
        private const uint StsHalted = 1U << 0;
        private const uint StsFatal = 1U << 2;
        private const uint StsCnr = 1U << 11;
        private const uint StsHce = 1U << 12;

        // PORTSC.
        private const uint PortConnected = 1U << 0;
        private const uint PortEnabled = 1U << 1;
        private const uint PortReset = 1U << 4;
        private const uint PortPower = 1U << 9;
        private const uint PortSpeedMask = 0xFU << 10;
        private const uint PortChangeBits = 0x7FU << 17;

        // xHCI TRB common bits/types.
        private const uint TrbCycle = 1U << 0;
        private const uint TrbToggleCycle = 1U << 1;
        private const uint TrbInterruptOnShortPacket = 1U << 2;
        private const uint TrbIoc = 1U << 5;
        private const uint TrbIdt = 1U << 6;
        private const int TrbTypeShift = 10;
        private const int TrbDirectionShift = 16;
        private const int TrbSlotIdShift = 24;

        private const uint TrbNormal = 1;
        private const uint TrbSetup = 2;
        private const uint TrbData = 3;
        private const uint TrbStatus = 4;
        private const uint TrbLink = 6;
        private const uint TrbEnableSlot = 9;
        private const uint TrbAddressDevice = 11;
        private const uint TrbConfigureEndpoint = 12;
        private const uint TrbEvaluateContext = 13;
        private const uint TrbTransferEvent = 32;
        private const uint TrbCommandCompletionEvent = 33;
        private const uint TrbPortStatusChangeEvent = 34;

        private const byte CompletionSuccess = 1;
        private const byte CompletionShortPacket = 13;

        // USB requests/descriptors/classes.
        private const byte UsbReqGetStatus = 0;
        private const byte UsbReqClearFeature = 1;
        private const byte UsbReqSetFeature = 3;
        private const byte UsbReqGetDescriptor = 6;
        private const byte UsbReqSetConfiguration = 9;
        private const byte HidReqSetIdle = 0x0A;
        private const byte HidReqSetProtocol = 0x0B;
        private const ushort DescriptorDevice = 1;
        private const ushort DescriptorConfiguration = 2;
        private const ushort DescriptorHub = 0x29;
        private const byte ClassHid = 3;
        private const byte ClassHub = 9;
        private const byte HidSubclassBoot = 1;
        private const byte HidProtocolKeyboard = 1;

        // USB2 hub features/status.
        private const ushort HubPortFeatureReset = 4;
        private const ushort HubPortFeaturePower = 8;
        private const ushort HubPortFeatureCConnection = 16;
        private const ushort HubPortFeatureCReset = 20;
        private const ushort HubPortStatusConnection = 1U << 0;
        private const ushort HubPortStatusEnable = 1U << 1;
        private const ushort HubPortStatusReset = 1U << 4;
        private const ushort HubPortStatusLowSpeed = 1U << 9;
        private const ushort HubPortStatusHighSpeed = 1U << 10;

        // ARM64 translation table bits for verifying the mappings created by the probe.
        private const ulong DescriptorValid = 1UL << 0;
        private const ulong DescriptorTable = 1UL << 1;
        private const ulong AddressMask = 0x0000FFFFFFFFF000UL;

        private static ulong _xhciBase;
        private static ulong _operationalBase;
        private static ulong _doorbellBase;
        private static ulong _runtimeBase;
        private static ulong _pciConfigBase;
        private static uint _maxSlots;
        private static uint _maxPorts;
        private static uint _hcsParams2;
        private static uint _hccParams1;
        private static int _contextSize;

        private static Rpi4DmaArena _dma = null!;
        private static ProducerRing _commandRing = null!;
        private static Trb* _eventRing;
        private static ulong _eventRingPhysical;
        private static int _eventIndex;
        private static uint _eventCycle;
        private static ulong* _dcbaa;
        private static nuint _dcbaaBytes;
        private static byte* _descriptorBuffer;

        private static UsbDevice _hub = null!;
        private static UsbDevice _keyboard = null!;
        private static ProducerRing _keyboardRing = null!;
        private static byte* _keyboardReport;
        private static bool _keyboardTransferPending;
        private static byte _keyboardEndpointDci;
        private static byte _keyboardSlotId;
        private static readonly byte[] _previousKeys = new byte[6];
        private static readonly char[] _keyQueue = new char[32];
        private static int _keyQueueHead;
        private static int _keyQueueTail;
        private static bool _capsLock;
        private static bool _initialized;

        public static bool IsReady => _initialized;

        public static bool Initialize()
        {
            _initialized = false;
            _keyboardTransferPending = false;
            _keyQueueHead = 0;
            _keyQueueTail = 0;
            _capsLock = false;
            Array.Clear(_previousKeys, 0, _previousKeys.Length);

            Console.WriteLine("========================================");
            Console.WriteLine(" RPi4 VL805 xHCI USB KEYBOARD");
            Console.WriteLine("========================================");

            if (!OpenPlatform(out string platformError))
            {
                Console.WriteLine("[USB] platform open: FAILED - " + platformError);
                return false;
            }

            Console.WriteLine("[USB] VL805 MMIO + PCI config: OK");

            if (!AcquireLegacyOwnership())
            {
                Console.WriteLine("[USB] xHCI ownership handoff: FAILED");
                return false;
            }

            if (!HaltAndResetController())
            {
                Console.WriteLine("[USB] xHCI reset: FAILED");
                return false;
            }

            Console.WriteLine("[USB] xHCI reset: OK");

            if (!Rpi4DmaArena.TryCreate(DmaArenaSize, out _dma, out string dmaError))
            {
                Console.WriteLine("[USB] DMA arena: FAILED - " + dmaError);
                return false;
            }

            Console.WriteLine("[USB] DMA arena: OK @ " + Hex(_dma.BasePhysical));

            if (!SetupControllerMemory())
            {
                Console.WriteLine("[USB] xHCI rings/contexts: FAILED");
                return false;
            }

            if (!EnablePciBusMaster())
            {
                Console.WriteLine("[USB] PCI bus mastering: FAILED");
                return false;
            }

            if (!StartController())
            {
                Console.WriteLine("[USB] xHCI run: FAILED");
                return false;
            }

            Console.WriteLine("[USB] xHCI command/event rings: RUNNING (polling)");

            // Raspberry Pi 4 routes every main-port USB2 signal through the VL805
            // integrated USB2 hub connected to xHCI root port 1.
            if (!PrepareRootUsb2Port(1, out byte hubSpeed))
            {
                Console.WriteLine("[USB] root USB2 hub port: FAILED");
                return false;
            }

            if (!AddressNewDevice(0, 1, hubSpeed, 0, 0, 0, out _hub))
            {
                Console.WriteLine("[USB] VIA hub Address Device: FAILED");
                return false;
            }

            if (!ReadDeviceAndConfiguration(_hub))
            {
                Console.WriteLine("[USB] VIA hub descriptors: FAILED");
                return false;
            }

            if (!_hub.IsHub)
            {
                Console.WriteLine("[USB] root port 1 device is not a USB hub");
                return false;
            }

            if (!SetConfiguration(_hub, _hub.ConfigurationValue))
            {
                Console.WriteLine("[USB] VIA hub SET_CONFIGURATION: FAILED");
                return false;
            }

            if (!InitializeHub(_hub))
            {
                Console.WriteLine("[USB] VIA USB2 hub init: FAILED");
                return false;
            }

            Console.WriteLine("[USB] VIA USB2 hub: OK, ports=" + _hub.HubPortCount);

            if (!FindKeyboardBehindHub(_hub))
            {
                Console.WriteLine("[USB] HID boot keyboard: NOT FOUND");
                Console.WriteLine("[USB] Connect the keyboard to a Pi 4 USB port and reboot for this stage.");
                return false;
            }

            if (!ConfigureKeyboard(_keyboard))
            {
                Console.WriteLine("[USB] HID keyboard configure: FAILED");
                return false;
            }

            Console.WriteLine("[USB] HID boot keyboard: READY");
            Console.WriteLine("[USB] input mode: xHCI Interrupt-IN polling (no xHCI IRQ/MSI)");
            _initialized = true;
            return true;
        }

        /// <summary>
        /// Non-blocking keyboard poll. Returns one decoded boot-keyboard character
        /// at a time. Enter is '\n' and Backspace is '\b'.
        /// </summary>
        public static bool TryReadChar(out char value)
        {
            value = '\0';
            if (!_initialized)
                return false;

            if (TryDequeueKey(out value))
                return true;

            PollKeyboardTransfer();
            return TryDequeueKey(out value);
        }

        private static bool OpenPlatform(out string error)
        {
            error = string.Empty;
            if (Limine.HHDM.Response == null)
            {
                error = "Limine HHDM missing";
                return false;
            }

            ulong hhdm = Limine.HHDM.Response->Offset;

            // The preceding physical probe normally established these Device mappings.
            // Re-run Cosmos' idempotent mapper and verify before dereferencing them.
            DeviceMapper.EnsureMapped(PcieRegPhysicalBase);
            if (!IsDeviceMapped(PcieRegPhysicalBase))
            {
                error = "BCM2711 PCIe registers are not Device-mapped";
                return false;
            }

            ulong pcieRegs = PcieRegPhysicalBase + hhdm;
            uint busNumbers = Read32(pcieRegs + 0x18);
            byte secondaryBus = (byte)((busNumbers >> 8) & 0xFFU);
            if (secondaryBus == 0)
            {
                error = "BCM2711 secondary PCI bus is zero";
                return false;
            }

            uint bdf = (uint)secondaryBus << 20;
            Write32(pcieRegs + PcieExtCfgIndexOffset, bdf);
            Rpi4PageTableNative.DsbIsb();
            _pciConfigBase = pcieRegs + PcieExtCfgDataOffset;

            uint id = Read32(_pciConfigBase);
            ushort vendor = (ushort)(id & 0xFFFFU);
            if (vendor == 0 || vendor == 0xFFFF)
            {
                error = "VL805 PCI config space not responding";
                return false;
            }

            uint classReg = Read32(_pciConfigBase + 0x08);
            byte progIf = (byte)((classReg >> 8) & 0xFFU);
            byte subclass = (byte)((classReg >> 16) & 0xFFU);
            byte classCode = (byte)((classReg >> 24) & 0xFFU);
            if (classCode != 0x0C || subclass != 0x03 || progIf != 0x30)
            {
                error = "downstream PCI function is not xHCI";
                return false;
            }

            uint barLow = Read32(_pciConfigBase + 0x10);
            if ((barLow & 1U) != 0)
            {
                error = "VL805 BAR0 is not MMIO";
                return false;
            }

            ulong pciBar = barLow & 0xFFFFFFF0U;
            uint barType = (barLow >> 1) & 0x3U;
            if (barType == 2)
                pciBar |= (ulong)Read32(_pciConfigBase + 0x14) << 32;

            if (pciBar < PcieBusMmioBase || pciBar >= PcieBusMmioBase + PcieBusMmioLength)
            {
                error = "VL805 BAR is outside PFTF PCI aperture";
                return false;
            }

            ulong xhciPhysical = PcieCpuMmioWindow + (pciBar - PcieBusMmioBase);
            DeviceMapper.EnsureMapped(xhciPhysical);
            if (!IsDeviceMapped(xhciPhysical))
            {
                // The hardened probe installs the otherwise-absent high L1 mapping.
                // Refuse to guess here if that verified stage did not complete.
                error = "VL805 high MMIO window is not Device-mapped; run probe stage first";
                return false;
            }

            _xhciBase = xhciPhysical + hhdm;
            uint cap0 = Read32(_xhciBase);
            byte capLength = (byte)(cap0 & 0xFFU);
            ushort version = (ushort)(cap0 >> 16);
            if (capLength < 0x20 || capLength > 0x80 || version < 0x0090 || version > 0x0200)
            {
                error = "invalid xHCI capability header";
                return false;
            }

            uint hcs1 = Read32(_xhciBase + HcsParams1Offset);
            _hcsParams2 = Read32(_xhciBase + HcsParams2Offset);
            _hccParams1 = Read32(_xhciBase + HccParams1Offset);
            _maxSlots = hcs1 & 0xFFU;
            _maxPorts = (hcs1 >> 24) & 0xFFU;
            if (_maxSlots == 0 || _maxSlots > 255 || _maxPorts == 0 || _maxPorts > 32)
            {
                error = "invalid xHCI slot/port counts";
                return false;
            }

            _contextSize = (_hccParams1 & (1U << 2)) != 0 ? 64 : 32;
            _operationalBase = _xhciBase + capLength;
            _doorbellBase = _xhciBase + (Read32(_xhciBase + DoorbellOffset) & 0xFFFFFFFCU);
            _runtimeBase = _xhciBase + (Read32(_xhciBase + RuntimeOffset) & 0xFFFFFFE0U);
            return true;
        }

        private static bool AcquireLegacyOwnership()
        {
            uint offset = ((_hccParams1 >> 16) & 0xFFFFU) * 4U;
            int guard = 0;
            while (offset != 0 && guard++ < 64)
            {
                ulong address = _xhciBase + offset;
                uint header = Read32(address);
                byte id = (byte)(header & 0xFFU);
                byte next = (byte)((header >> 8) & 0xFFU);

                if (id == 1)
                {
                    // USB Legacy Support: request OS ownership and wait for firmware.
                    uint legacy = header | (1U << 24);
                    Write32(address, legacy);
                    for (int i = 0; i < 1000; i++)
                    {
                        uint current = Read32(address);
                        if ((current & (1U << 16)) == 0)
                            return true;
                        Rpi4DmaArena.DelayMilliseconds(1);
                    }
                    return false;
                }

                if (next == 0)
                    break;
                offset += (uint)next * 4U;
            }

            // No legacy capability means no firmware semaphore to hand off.
            return true;
        }

        private static bool HaltAndResetController()
        {
            uint cmd = Read32(_operationalBase + UsbCmdOffset);
            if ((cmd & CmdRun) != 0)
            {
                Write32(_operationalBase + UsbCmdOffset, cmd & ~CmdRun);
                if (!WaitRegisterBits(_operationalBase + UsbStsOffset, StsHalted, StsHalted, 2000))
                    return false;
            }

            Write32(_operationalBase + UsbCmdOffset, CmdReset);
            if (!WaitRegisterBits(_operationalBase + UsbCmdOffset, CmdReset, 0, 5000))
                return false;
            if (!WaitRegisterBits(_operationalBase + UsbStsOffset, StsCnr, 0, 5000))
                return false;

            uint pageSize = Read32(_operationalBase + PageSizeOffset);
            return (pageSize & 1U) != 0;
        }

        private static bool SetupControllerMemory()
        {
            _dcbaaBytes = (nuint)((_maxSlots + 1U) * 8U);
            _dcbaa = (ulong*)_dma.Alloc(_dcbaaBytes, 64);
            if (_dcbaa == null)
                return false;

            uint scratchpadCount = (((_hcsParams2 >> 21) & 0x1FU) << 5) |
                                   ((_hcsParams2 >> 27) & 0x1FU);
            if (scratchpadCount > 64)
            {
                Console.WriteLine("[USB] unsupported scratchpad count: " + scratchpadCount);
                return false;
            }

            if (scratchpadCount != 0)
            {
                ulong* scratchpadArray = (ulong*)_dma.Alloc((nuint)(scratchpadCount * 8U), 64);
                if (scratchpadArray == null)
                    return false;

                for (uint i = 0; i < scratchpadCount; i++)
                {
                    void* page = _dma.Alloc(4096, 4096);
                    if (page == null)
                        return false;
                    scratchpadArray[i] = _dma.Physical(page);
                    Rpi4DmaArena.CleanInvalidate(page, 4096);
                }

                Rpi4DmaArena.Clean(scratchpadArray, (nuint)(scratchpadCount * 8U));
                _dcbaa[0] = _dma.Physical(scratchpadArray);
            }

            _commandRing = new ProducerRing(_dma, CommandRingTrbs);

            _eventRing = (Trb*)_dma.Alloc((nuint)(EventRingTrbs * sizeof(Trb)), 64);
            if (_eventRing == null)
                return false;
            _eventRingPhysical = _dma.Physical(_eventRing);
            _eventIndex = 0;
            _eventCycle = 1;
            Rpi4DmaArena.CleanInvalidate(_eventRing, (nuint)(EventRingTrbs * sizeof(Trb)));

            ErstEntry* erst = (ErstEntry*)_dma.Alloc((nuint)sizeof(ErstEntry), 64);
            if (erst == null)
                return false;
            erst->SegmentBase = _eventRingPhysical;
            erst->SegmentSize = EventRingTrbs;
            erst->Reserved = 0;
            Rpi4DmaArena.Clean(erst, (nuint)sizeof(ErstEntry));

            _descriptorBuffer = (byte*)_dma.Alloc(DescriptorBufferSize, 64);
            if (_descriptorBuffer == null)
                return false;

            Rpi4DmaArena.Clean(_dcbaa, _dcbaaBytes);

            Write64Register(_operationalBase + DcbaapOffset, _dma.Physical(_dcbaa));
            Write64Register(_operationalBase + CrcrOffset, _commandRing.PhysicalBase | 1UL);
            Write32(_operationalBase + ConfigOffset, _maxSlots & 0xFFU);

            ulong ir0 = _runtimeBase + Ir0BaseOffset;
            Write32(ir0 + ImanOffset, 1U); // clear pending, keep IE off
            Write32(ir0 + ImodOffset, 0U);
            Write32(ir0 + ErstszOffset, 1U);
            Write64Register(ir0 + ErstbaOffset, _dma.Physical(erst));
            Write64Register(ir0 + ErdpOffset, _eventRingPhysical);
            Rpi4PageTableNative.DsbIsb();
            return true;
        }

        private static bool EnablePciBusMaster()
        {
            ushort before = Read16(_pciConfigBase + 0x04);
            ushort wanted = (ushort)(before | PciCommandMemorySpace | PciCommandBusMaster);
            if (wanted != before)
            {
                Write16(_pciConfigBase + 0x04, wanted);
                Rpi4PageTableNative.DsbIsb();
            }

            ushort after = Read16(_pciConfigBase + 0x04);
            Console.WriteLine("[USB] PCI COMMAND: " + Hex(after));
            return (after & (PciCommandMemorySpace | PciCommandBusMaster)) ==
                   (PciCommandMemorySpace | PciCommandBusMaster);
        }

        private static bool StartController()
        {
            uint cmd = Read32(_operationalBase + UsbCmdOffset);
            Write32(_operationalBase + UsbCmdOffset, cmd | CmdRun);
            if (!WaitRegisterBits(_operationalBase + UsbStsOffset, StsHalted, 0, 2000))
                return false;

            uint status = Read32(_operationalBase + UsbStsOffset);
            return (status & (StsFatal | StsHce | StsCnr)) == 0;
        }

        private static bool PrepareRootUsb2Port(byte portId, out byte speed)
        {
            speed = 0;
            if (portId == 0 || portId > _maxPorts)
                return false;

            ulong portScAddress = PortScAddress(portId);
            uint value = Read32(portScAddress);

            if ((_hccParams1 & (1U << 3)) != 0 && (value & PortPower) == 0)
            {
                Write32(portScAddress, PortPower);
                Rpi4DmaArena.DelayMilliseconds(20);
                value = Read32(portScAddress);
            }

            if ((value & PortConnected) == 0)
                return false;

            // USB2 port reset. Do not echo PED back as one: PED is RW1C on xHCI.
            Write32(portScAddress, (value & PortPower) | PortReset);
            for (int i = 0; i < 1000; i++)
            {
                value = Read32(portScAddress);
                if ((value & PortReset) == 0 && (value & PortEnabled) != 0)
                    break;
                Rpi4DmaArena.DelayMilliseconds(1);
            }

            value = Read32(portScAddress);
            if ((value & PortConnected) == 0 || (value & PortEnabled) == 0 || (value & PortReset) != 0)
                return false;

            speed = (byte)((value & PortSpeedMask) >> 10);
            if (speed == 0)
                return false;

            uint changes = value & PortChangeBits;
            if (changes != 0)
                Write32(portScAddress, (value & PortPower) | changes);

            Console.WriteLine("[USB] root port " + portId + " speed-id=" + speed + " PORTSC=" + Hex(value));
            return true;
        }

        private static bool AddressNewDevice(
            uint routeString,
            byte rootPort,
            byte speed,
            byte ttHubSlot,
            byte ttPort,
            byte ttThinkTime,
            out UsbDevice device)
        {
            device = null!;
            if (!IssueCommand(TrbEnableSlot, 0, 0, out byte slotId))
                return false;
            if (slotId == 0 || slotId > _maxSlots)
                return false;

            var created = new UsbDevice
            {
                SlotId = slotId,
                RouteString = routeString,
                RootPort = rootPort,
                Speed = speed,
                TtHubSlot = ttHubSlot,
                TtPort = ttPort,
                TtThinkTime = ttThinkTime,
                MaxPacket0 = DefaultEp0PacketSize(speed)
            };

            created.DeviceContext = (byte*)_dma.Alloc((nuint)(_contextSize * 32), 64);
            created.InputContext = (byte*)_dma.Alloc((nuint)(_contextSize * 33), 64);
            if (created.DeviceContext == null || created.InputContext == null)
                return false;

            created.Ep0Ring = new ProducerRing(_dma, TransferRingTrbs);
            Rpi4DmaArena.CleanInvalidate(created.DeviceContext, (nuint)(_contextSize * 32));

            _dcbaa[slotId] = _dma.Physical(created.DeviceContext);
            Rpi4DmaArena.Clean(&_dcbaa[slotId], 8);

            BuildAddressInputContext(created);
            ulong inputPhysical = _dma.Physical(created.InputContext);
            if (!IssueCommand(TrbAddressDevice, inputPhysical, slotId, out _))
                return false;

            Rpi4DmaArena.Invalidate(created.DeviceContext, (nuint)(_contextSize * 32));
            device = created;
            return true;
        }

        private static void BuildAddressInputContext(UsbDevice device)
        {
            nuint bytes = (nuint)(_contextSize * 33);
            Clear(device.InputContext, bytes);
            uint* control = (uint*)device.InputContext;
            control[1] = 0x3U; // Add Slot + EP0 contexts.

            uint* slot = (uint*)(device.InputContext + _contextSize);
            slot[0] = (device.RouteString & 0xFFFFFU) |
                      ((uint)device.Speed << 20) |
                      (1U << 27); // Context Entries = 1 (EP0)
            slot[1] = (uint)device.RootPort << 16;

            if (device.TtHubSlot != 0 && (device.Speed == 1 || device.Speed == 2))
            {
                slot[2] = device.TtHubSlot |
                          ((uint)device.TtPort << 8) |
                          ((uint)(device.TtThinkTime & 0x3) << 16);
            }

            uint* ep0 = (uint*)(device.InputContext + _contextSize * 2);
            ep0[1] = (3U << 1) | (4U << 3) | ((uint)device.MaxPacket0 << 16);
            ulong dequeue = device.Ep0Ring.PhysicalBase | 1UL;
            ep0[2] = (uint)dequeue;
            ep0[3] = (uint)(dequeue >> 32);
            ep0[4] = 8U;

            Rpi4DmaArena.Clean(device.InputContext, bytes);
        }

        private static bool ReadDeviceAndConfiguration(UsbDevice device)
        {
            if (!ControlIn(device, 0x80, UsbReqGetDescriptor,
                    (ushort)(DescriptorDevice << 8), 0, _descriptorBuffer, 18))
                return false;

            device.DeviceClass = _descriptorBuffer[4];
            device.DeviceProtocol = _descriptorBuffer[6];
            device.VendorId = ReadLe16(_descriptorBuffer + 8);
            device.ProductId = ReadLe16(_descriptorBuffer + 10);

            ushort reportedMps;
            if (device.Speed >= 4)
                reportedMps = (ushort)(1U << _descriptorBuffer[7]);
            else
                reportedMps = _descriptorBuffer[7];

            if (reportedMps != 0 && reportedMps != device.MaxPacket0)
            {
                if (!UpdateEp0PacketSize(device, reportedMps))
                    return false;
                device.MaxPacket0 = reportedMps;
            }

            if (!ControlIn(device, 0x80, UsbReqGetDescriptor,
                    (ushort)(DescriptorConfiguration << 8), 0, _descriptorBuffer, 9))
                return false;

            ushort totalLength = ReadLe16(_descriptorBuffer + 2);
            if (totalLength < 9 || totalLength > DescriptorBufferSize)
                return false;

            if (!ControlIn(device, 0x80, UsbReqGetDescriptor,
                    (ushort)(DescriptorConfiguration << 8), 0, _descriptorBuffer, totalLength))
                return false;

            device.ConfigurationValue = _descriptorBuffer[5];
            ParseConfiguration(device, _descriptorBuffer, totalLength);

            Console.WriteLine("[USB] slot " + device.SlotId +
                              " vid:pid=" + Hex(device.VendorId) + ":" + Hex(device.ProductId) +
                              " class=" + Hex(device.DeviceClass));
            return true;
        }

        private static void ParseConfiguration(UsbDevice device, byte* buffer, int length)
        {
            byte currentInterface = 0;
            bool currentKeyboard = false;
            int offset = 0;

            while (offset + 2 <= length)
            {
                byte descriptorLength = buffer[offset];
                byte descriptorType = buffer[offset + 1];
                if (descriptorLength < 2 || offset + descriptorLength > length)
                    break;

                if (descriptorType == 4 && descriptorLength >= 9)
                {
                    currentInterface = buffer[offset + 2];
                    byte interfaceClass = buffer[offset + 5];
                    byte interfaceSubclass = buffer[offset + 6];
                    byte interfaceProtocol = buffer[offset + 7];

                    if (interfaceClass == ClassHub)
                        device.IsHub = true;

                    currentKeyboard = interfaceClass == ClassHid &&
                                      interfaceSubclass == HidSubclassBoot &&
                                      interfaceProtocol == HidProtocolKeyboard;
                    if (currentKeyboard)
                    {
                        device.IsKeyboard = true;
                        device.KeyboardInterface = currentInterface;
                    }
                }
                else if (descriptorType == 5 && descriptorLength >= 7 && currentKeyboard)
                {
                    byte endpointAddress = buffer[offset + 2];
                    byte attributes = buffer[offset + 3];
                    if ((endpointAddress & 0x80) != 0 && (attributes & 0x3) == 0x3)
                    {
                        device.KeyboardEndpointAddress = endpointAddress;
                        device.KeyboardEndpointMaxPacket = (ushort)(ReadLe16(buffer + offset + 4) & 0x07FFU);
                        device.KeyboardEndpointInterval = buffer[offset + 6];
                    }
                }

                offset += descriptorLength;
            }

            if (device.DeviceClass == ClassHub)
                device.IsHub = true;
        }

        private static bool UpdateEp0PacketSize(UsbDevice device, ushort maxPacket)
        {
            Clear(device.InputContext, (nuint)(_contextSize * 33));
            Rpi4DmaArena.Invalidate(device.DeviceContext, (nuint)(_contextSize * 32));

            uint* control = (uint*)device.InputContext;
            control[1] = 1U << 1; // EP0 only.

            byte* deviceEp0 = device.DeviceContext + _contextSize;
            byte* inputEp0 = device.InputContext + _contextSize * 2;
            Copy(inputEp0, deviceEp0, (nuint)_contextSize);
            uint* ep = (uint*)inputEp0;
            ep[1] = (ep[1] & 0x0000FFFFU) | ((uint)maxPacket << 16);

            Rpi4DmaArena.Clean(device.InputContext, (nuint)(_contextSize * 33));
            return IssueCommand(TrbEvaluateContext, _dma.Physical(device.InputContext), device.SlotId, out _);
        }

        private static bool SetConfiguration(UsbDevice device, byte configurationValue)
        {
            return ControlNoData(device, 0x00, UsbReqSetConfiguration, configurationValue, 0);
        }

        private static bool InitializeHub(UsbDevice hub)
        {
            if (!ControlIn(hub, 0xA0, UsbReqGetDescriptor,
                    (ushort)(DescriptorHub << 8), 0, _descriptorBuffer, 7))
                return false;

            byte portCount = _descriptorBuffer[2];
            if (portCount == 0 || portCount > 15)
                return false;

            ushort characteristics = ReadLe16(_descriptorBuffer + 3);
            byte powerGoodUnits = _descriptorBuffer[5];
            hub.HubPortCount = portCount;
            hub.HubTtThinkTime = (byte)((characteristics >> 5) & 0x3);
            hub.HubMultiTt = hub.DeviceProtocol == 2;

            if (!MarkDeviceAsHub(hub))
                return false;

            for (byte port = 1; port <= portCount; port++)
            {
                if (!ControlNoData(hub, 0x23, UsbReqSetFeature, HubPortFeaturePower, port))
                    return false;
            }

            uint delayMs = (uint)powerGoodUnits * 2U;
            if (delayMs < 20)
                delayMs = 20;
            Rpi4DmaArena.DelayMilliseconds(delayMs);
            return true;
        }

        private static bool MarkDeviceAsHub(UsbDevice hub)
        {
            Clear(hub.InputContext, (nuint)(_contextSize * 33));
            Rpi4DmaArena.Invalidate(hub.DeviceContext, (nuint)(_contextSize * 32));

            uint* control = (uint*)hub.InputContext;
            control[1] = 1U; // Slot Context only.

            byte* inputSlot = hub.InputContext + _contextSize;
            Copy(inputSlot, hub.DeviceContext, (nuint)_contextSize);
            uint* slot = (uint*)inputSlot;
            slot[0] |= 1U << 26; // Hub bit.
            if (hub.HubMultiTt)
                slot[0] |= 1U << 25;
            else
                slot[0] &= ~(1U << 25);
            slot[1] = (slot[1] & 0x00FFFFFFU) | ((uint)hub.HubPortCount << 24);
            slot[2] = (slot[2] & ~(0x3U << 16)) | ((uint)hub.HubTtThinkTime << 16);

            Rpi4DmaArena.Clean(hub.InputContext, (nuint)(_contextSize * 33));
            return IssueCommand(TrbEvaluateContext, _dma.Physical(hub.InputContext), hub.SlotId, out _);
        }

        private static bool FindKeyboardBehindHub(UsbDevice hub)
        {
            for (byte port = 1; port <= hub.HubPortCount; port++)
            {
                if (!GetHubPortStatus(hub, port, out ushort status))
                    continue;
                if ((status & HubPortStatusConnection) == 0)
                    continue;

                Console.WriteLine("[USB] hub port " + port + ": device connected");
                if (!ResetHubPort(hub, port, out byte speed))
                {
                    Console.WriteLine("[USB] hub port " + port + ": reset failed");
                    continue;
                }

                uint route = port & 0xFU;
                byte ttSlot = 0;
                byte ttPort = 0;
                byte ttThink = 0;
                if (speed == 1 || speed == 2)
                {
                    ttSlot = hub.SlotId;
                    ttPort = port;
                    ttThink = hub.HubTtThinkTime;
                }

                if (!AddressNewDevice(route, hub.RootPort, speed, ttSlot, ttPort, ttThink, out UsbDevice child))
                {
                    Console.WriteLine("[USB] hub port " + port + ": Address Device failed");
                    continue;
                }

                if (!ReadDeviceAndConfiguration(child))
                {
                    Console.WriteLine("[USB] hub port " + port + ": descriptor read failed");
                    continue;
                }

                if (!child.IsKeyboard || child.KeyboardEndpointAddress == 0)
                {
                    Console.WriteLine("[USB] hub port " + port + ": not a boot keyboard");
                    continue;
                }

                _keyboard = child;
                return true;
            }

            return false;
        }

        private static bool GetHubPortStatus(UsbDevice hub, byte port, out ushort status)
        {
            status = 0;
            if (!ControlIn(hub, 0xA3, UsbReqGetStatus, 0, port, _descriptorBuffer, 4))
                return false;
            status = ReadLe16(_descriptorBuffer);
            return true;
        }

        private static bool ResetHubPort(UsbDevice hub, byte port, out byte speed)
        {
            speed = 0;
            // Clear stale connection change if firmware left it set.
            ControlNoData(hub, 0x23, UsbReqClearFeature, HubPortFeatureCConnection, port);

            if (!ControlNoData(hub, 0x23, UsbReqSetFeature, HubPortFeatureReset, port))
                return false;

            ushort status = 0;
            bool ready = false;
            for (int i = 0; i < 100; i++)
            {
                Rpi4DmaArena.DelayMilliseconds(10);
                if (!GetHubPortStatus(hub, port, out status))
                    return false;
                if ((status & HubPortStatusReset) == 0 && (status & HubPortStatusEnable) != 0)
                {
                    ready = true;
                    break;
                }
            }

            if (!ready || (status & HubPortStatusConnection) == 0)
                return false;

            ControlNoData(hub, 0x23, UsbReqClearFeature, HubPortFeatureCReset, port);
            Rpi4DmaArena.DelayMilliseconds(10);

            if ((status & HubPortStatusLowSpeed) != 0)
                speed = 2; // xHCI Low Speed ID
            else if ((status & HubPortStatusHighSpeed) != 0)
                speed = 3; // xHCI High Speed ID
            else
                speed = 1; // xHCI Full Speed ID

            Console.WriteLine("[USB] hub port " + port + ": speed-id=" + speed);
            return true;
        }

        private static bool ConfigureKeyboard(UsbDevice keyboard)
        {
            if (keyboard.KeyboardEndpointMaxPacket == 0)
                return false;

            if (!SetConfiguration(keyboard, keyboard.ConfigurationValue))
                return false;

            // Force the standard 8-byte boot report independent of the device's
            // report descriptor, then disable idle repeats; key repeat can be added
            // later in the shell layer if wanted.
            if (!ControlNoData(keyboard, 0x21, HidReqSetProtocol, 0, keyboard.KeyboardInterface))
                return false;
            ControlNoData(keyboard, 0x21, HidReqSetIdle, 0, keyboard.KeyboardInterface);

            byte endpointNumber = (byte)(keyboard.KeyboardEndpointAddress & 0x0F);
            if (endpointNumber == 0)
                return false;
            byte dci = (byte)(endpointNumber * 2 + 1); // IN endpoint DCI.
            if (dci > 31)
                return false;

            _keyboardRing = new ProducerRing(_dma, TransferRingTrbs);
            _keyboardReport = (byte*)_dma.Alloc(64, 64);
            if (_keyboardReport == null)
                return false;

            Clear(keyboard.InputContext, (nuint)(_contextSize * 33));
            Rpi4DmaArena.Invalidate(keyboard.DeviceContext, (nuint)(_contextSize * 32));

            uint* control = (uint*)keyboard.InputContext;
            control[1] = 1U | (1U << dci); // Slot + Interrupt IN endpoint.

            byte* inputSlot = keyboard.InputContext + _contextSize;
            Copy(inputSlot, keyboard.DeviceContext, (nuint)_contextSize);
            uint* slot = (uint*)inputSlot;
            uint existingEntries = (slot[0] >> 27) & 0x1FU;
            if (existingEntries < dci)
                slot[0] = (slot[0] & 0x07FFFFFFU) | ((uint)dci << 27);

            byte* endpointContext = keyboard.InputContext + _contextSize * (dci + 1);
            uint* ep = (uint*)endpointContext;
            uint interval = EncodeInterval(keyboard.Speed, keyboard.KeyboardEndpointInterval);
            ushort maxPacket = keyboard.KeyboardEndpointMaxPacket;
            ep[0] = interval << 16;
            ep[1] = (3U << 1) | (7U << 3) | ((uint)maxPacket << 16); // CErr=3, Interrupt IN.
            ulong dequeue = _keyboardRing.PhysicalBase | 1UL;
            ep[2] = (uint)dequeue;
            ep[3] = (uint)(dequeue >> 32);
            ep[4] = (uint)maxPacket | ((uint)maxPacket << 16);

            Rpi4DmaArena.Clean(keyboard.InputContext, (nuint)(_contextSize * 33));
            if (!IssueCommand(TrbConfigureEndpoint, _dma.Physical(keyboard.InputContext), keyboard.SlotId, out _))
                return false;

            _keyboardEndpointDci = dci;
            _keyboardSlotId = keyboard.SlotId;
            _keyboardTransferPending = false;
            SubmitKeyboardTransfer();
            return true;
        }

        private static uint EncodeInterval(byte speed, byte bInterval)
        {
            if (bInterval == 0)
                bInterval = 1;

            if (speed >= 3)
            {
                uint value = (uint)bInterval - 1U;
                return value > 15U ? 15U : value;
            }

            // Full/Low-speed bInterval is in 1 ms frames. xHCI wants an exponent
            // in 125 us microframes, so round up log2(bInterval * 8).
            uint microframes = (uint)bInterval * 8U;
            uint exponent = 0;
            uint period = 1;
            while (period < microframes && exponent < 15)
            {
                period <<= 1;
                exponent++;
            }
            return exponent;
        }

        private static void SubmitKeyboardTransfer()
        {
            if (_keyboardTransferPending || _keyboardReport == null)
                return;

            Clear(_keyboardReport, 64);
            Rpi4DmaArena.CleanInvalidate(_keyboardReport, 64);
            ulong reportPhysical = _dma.Physical(_keyboardReport);
            uint control = (TrbNormal << TrbTypeShift) | TrbIoc | TrbInterruptOnShortPacket;
            _keyboardRing.Enqueue(reportPhysical, 8U, control);
            Rpi4PageTableNative.DsbIsb();
            RingDoorbell(_keyboardSlotId, _keyboardEndpointDci);
            _keyboardTransferPending = true;
        }

        private static void PollKeyboardTransfer()
        {
            if (!_keyboardTransferPending)
            {
                SubmitKeyboardTransfer();
                return;
            }

            int guard = 0;
            while (guard++ < 8 && TryPopEvent(out Trb evt))
            {
                uint type = (evt.Control >> TrbTypeShift) & 0x3FU;
                if (type != TrbTransferEvent)
                    continue;

                byte slot = (byte)(evt.Control >> 24);
                byte endpoint = (byte)((evt.Control >> 16) & 0x1FU);
                if (slot != _keyboardSlotId || endpoint != _keyboardEndpointDci)
                    continue;

                byte completion = (byte)(evt.Status >> 24);
                _keyboardTransferPending = false;
                if (completion == CompletionSuccess || completion == CompletionShortPacket)
                {
                    Rpi4DmaArena.Invalidate(_keyboardReport, 64);
                    DecodeBootKeyboardReport(_keyboardReport);
                }
                else
                {
                    Console.WriteLine();
                    Console.WriteLine("[USB] keyboard transfer completion=" + completion);
                }

                SubmitKeyboardTransfer();
                break;
            }
        }

        private static void DecodeBootKeyboardReport(byte* report)
        {
            byte modifiers = report[0];
            bool shift = (modifiers & 0x22) != 0; // Left or right Shift.

            byte[] current = new byte[6];
            for (int i = 0; i < 6; i++)
                current[i] = report[i + 2];

            for (int i = 0; i < 6; i++)
            {
                byte usage = current[i];
                if (usage == 0 || ContainsUsage(_previousKeys, usage))
                    continue;

                if (usage == 0x39)
                {
                    _capsLock = !_capsLock;
                    continue;
                }

                char decoded = DecodeUsage(usage, shift, _capsLock);
                if (decoded != '\0')
                    EnqueueKey(decoded);
            }

            for (int i = 0; i < 6; i++)
                _previousKeys[i] = current[i];
        }

        private static bool ContainsUsage(byte[] values, byte usage)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == usage)
                    return true;
            }
            return false;
        }

        private static char DecodeUsage(byte usage, bool shift, bool capsLock)
        {
            if (usage >= 0x04 && usage <= 0x1D)
            {
                char lower = (char)('a' + usage - 0x04);
                bool upper = shift ^ capsLock;
                return upper ? (char)(lower - 32) : lower;
            }

            const string digits = "1234567890";
            const string shiftedDigits = "!@#$%^&*()";
            if (usage >= 0x1E && usage <= 0x27)
            {
                int index = usage - 0x1E;
                return shift ? shiftedDigits[index] : digits[index];
            }

            switch (usage)
            {
                case 0x28: return '\n';
                case 0x2A: return '\b';
                case 0x2B: return '\t';
                case 0x2C: return ' ';
                case 0x2D: return shift ? '_' : '-';
                case 0x2E: return shift ? '+' : '=';
                case 0x2F: return shift ? '{' : '[';
                case 0x30: return shift ? '}' : ']';
                case 0x31: return shift ? '|' : '\\';
                case 0x33: return shift ? ':' : ';';
                case 0x34: return shift ? '"' : '\'';
                case 0x35: return shift ? '~' : '`';
                case 0x36: return shift ? '<' : ',';
                case 0x37: return shift ? '>' : '.';
                case 0x38: return shift ? '?' : '/';
                default: return '\0';
            }
        }

        private static void EnqueueKey(char value)
        {
            int next = (_keyQueueTail + 1) % _keyQueue.Length;
            if (next == _keyQueueHead)
                return;
            _keyQueue[_keyQueueTail] = value;
            _keyQueueTail = next;
        }

        private static bool TryDequeueKey(out char value)
        {
            if (_keyQueueHead == _keyQueueTail)
            {
                value = '\0';
                return false;
            }

            value = _keyQueue[_keyQueueHead];
            _keyQueueHead = (_keyQueueHead + 1) % _keyQueue.Length;
            return true;
        }

        private static bool ControlIn(
            UsbDevice device,
            byte requestType,
            byte request,
            ushort value,
            ushort index,
            byte* buffer,
            int length)
        {
            if (length <= 0)
                return false;

            ulong setup = BuildSetupPacket(requestType, request, value, index, (ushort)length);
            uint setupControl = (TrbSetup << TrbTypeShift) | TrbIdt | (3U << 16); // TRT=IN
            device.Ep0Ring.Enqueue(setup, 8U, setupControl);

            Rpi4DmaArena.CleanInvalidate(buffer, (nuint)length);
            uint dataControl = (TrbData << TrbTypeShift) | (1U << TrbDirectionShift);
            device.Ep0Ring.Enqueue(_dma.Physical(buffer), (uint)length, dataControl);

            uint statusControl = (TrbStatus << TrbTypeShift) | TrbIoc; // OUT status for IN transfer.
            ulong statusTrb = device.Ep0Ring.Enqueue(0, 0, statusControl);

            Rpi4PageTableNative.DsbIsb();
            RingDoorbell(device.SlotId, 1);
            if (!WaitTransferCompletion(device.SlotId, 1, statusTrb))
                return false;

            Rpi4DmaArena.Invalidate(buffer, (nuint)length);
            return true;
        }

        private static bool ControlNoData(
            UsbDevice device,
            byte requestType,
            byte request,
            ushort value,
            ushort index)
        {
            ulong setup = BuildSetupPacket(requestType, request, value, index, 0);
            uint setupControl = (TrbSetup << TrbTypeShift) | TrbIdt; // TRT=No Data
            device.Ep0Ring.Enqueue(setup, 8U, setupControl);

            uint statusControl = (TrbStatus << TrbTypeShift) | TrbIoc | (1U << TrbDirectionShift);
            ulong statusTrb = device.Ep0Ring.Enqueue(0, 0, statusControl);

            Rpi4PageTableNative.DsbIsb();
            RingDoorbell(device.SlotId, 1);
            return WaitTransferCompletion(device.SlotId, 1, statusTrb);
        }

        private static ulong BuildSetupPacket(byte requestType, byte request, ushort value, ushort index, ushort length)
        {
            return requestType |
                   ((ulong)request << 8) |
                   ((ulong)value << 16) |
                   ((ulong)index << 32) |
                   ((ulong)length << 48);
        }

        private static bool IssueCommand(uint type, ulong parameter, byte slotId, out byte resultSlot)
        {
            resultSlot = 0;
            uint control = type << TrbTypeShift;
            if (slotId != 0)
                control |= (uint)slotId << TrbSlotIdShift;

            ulong commandPhysical = _commandRing.Enqueue(parameter, 0, control);
            Rpi4PageTableNative.DsbIsb();
            RingDoorbell(0, 0);

            for (int i = 0; i < 40000; i++)
            {
                if (TryPopEvent(out Trb evt))
                {
                    uint eventType = (evt.Control >> TrbTypeShift) & 0x3FU;
                    if (eventType == TrbPortStatusChangeEvent)
                        continue;
                    if (eventType != TrbCommandCompletionEvent)
                        continue;

                    ulong pointer = evt.Parameter & ~0xFUL;
                    if (pointer != (commandPhysical & ~0xFUL))
                        continue;

                    byte completion = (byte)(evt.Status >> 24);
                    resultSlot = (byte)(evt.Control >> 24);
                    return completion == CompletionSuccess;
                }

                Rpi4DmaArena.DelayMicroseconds(50);
            }

            return false;
        }

        private static bool WaitTransferCompletion(byte slotId, byte endpointDci, ulong expectedTrb)
        {
            for (int i = 0; i < 40000; i++)
            {
                if (TryPopEvent(out Trb evt))
                {
                    uint type = (evt.Control >> TrbTypeShift) & 0x3FU;
                    if (type == TrbPortStatusChangeEvent)
                        continue;
                    if (type != TrbTransferEvent)
                        continue;

                    byte slot = (byte)(evt.Control >> 24);
                    byte endpoint = (byte)((evt.Control >> 16) & 0x1FU);
                    if (slot != slotId || endpoint != endpointDci)
                        continue;

                    ulong pointer = evt.Parameter & ~0xFUL;
                    if (pointer != (expectedTrb & ~0xFUL))
                        continue;

                    byte completion = (byte)(evt.Status >> 24);
                    return completion == CompletionSuccess || completion == CompletionShortPacket;
                }

                Rpi4DmaArena.DelayMicroseconds(50);
            }

            return false;
        }

        private static bool TryPopEvent(out Trb value)
        {
            Trb* current = &_eventRing[_eventIndex];
            Rpi4DmaArena.Invalidate(current, (nuint)sizeof(Trb));
            if ((current->Control & TrbCycle) != _eventCycle)
            {
                value = default;
                return false;
            }

            value = *current;
            _eventIndex++;
            if (_eventIndex == EventRingTrbs)
            {
                _eventIndex = 0;
                _eventCycle ^= 1U;
            }

            ulong dequeue = _eventRingPhysical + (ulong)(_eventIndex * sizeof(Trb));
            Write64Register(_runtimeBase + Ir0BaseOffset + ErdpOffset, dequeue | (1UL << 3));
            return true;
        }

        private static void RingDoorbell(byte slotId, byte endpointTarget)
        {
            Write32(_doorbellBase + (ulong)slotId * 4UL, endpointTarget);
        }

        private static bool WaitRegisterBits(ulong address, uint mask, uint expected, int timeoutMilliseconds)
        {
            int loops = timeoutMilliseconds * 20;
            for (int i = 0; i < loops; i++)
            {
                if ((Read32(address) & mask) == expected)
                    return true;
                Rpi4DmaArena.DelayMicroseconds(50);
            }
            return false;
        }

        private static ulong PortScAddress(byte portId)
        {
            return _operationalBase + PortBaseOffset + (ulong)(portId - 1) * PortStride;
        }

        private static ushort DefaultEp0PacketSize(byte speed)
        {
            switch (speed)
            {
                case 3: return 64;  // High speed
                case 4: return 512; // SuperSpeed
                case 5: return 512; // SuperSpeedPlus
                default: return 8;  // Full/Low initial default
            }
        }

        private static bool IsDeviceMapped(ulong physicalAddress)
        {
            if (Limine.HHDM.Response == null)
                return false;

            ulong hhdm = Limine.HHDM.Response->Offset;
            ulong mair = Rpi4PageTableNative.ReadMair();
            ulong ttbr1 = Rpi4PageTableNative.ReadTtbr1() & AddressMask;
            if (ttbr1 == 0)
                return false;

            ulong* l0 = (ulong*)(ttbr1 + hhdm);
            ulong l0Entry = l0[(physicalAddress >> 39) & 0x1FFUL];
            if ((l0Entry & (DescriptorValid | DescriptorTable)) != (DescriptorValid | DescriptorTable))
                return false;

            ulong* l1 = (ulong*)((l0Entry & AddressMask) + hhdm);
            ulong l1Entry = l1[(physicalAddress >> 30) & 0x1FFUL];
            if ((l1Entry & DescriptorValid) == 0)
                return false;

            ulong descriptor;
            if ((l1Entry & DescriptorTable) == 0)
            {
                descriptor = l1Entry;
            }
            else
            {
                ulong* l2 = (ulong*)((l1Entry & AddressMask) + hhdm);
                ulong l2Entry = l2[(physicalAddress >> 21) & 0x1FFUL];
                if ((l2Entry & DescriptorValid) == 0 || (l2Entry & DescriptorTable) != 0)
                    return false;
                descriptor = l2Entry;
            }

            int attrIndex = (int)((descriptor >> 2) & 0x7UL);
            byte attr = (byte)((mair >> (attrIndex * 8)) & 0xFFUL);
            return attr == 0x00 || attr == 0x04;
        }

        private static void Clear(void* pointer, nuint length)
        {
            byte* p = (byte*)pointer;
            for (nuint i = 0; i < length; i++)
                p[i] = 0;
        }

        private static void Copy(void* destination, void* source, nuint length)
        {
            byte* dst = (byte*)destination;
            byte* src = (byte*)source;
            for (nuint i = 0; i < length; i++)
                dst[i] = src[i];
        }

        private static ushort ReadLe16(byte* address)
        {
            return (ushort)(address[0] | (address[1] << 8));
        }

        private static void Write64Register(ulong address, ulong value)
        {
            // PFTF's XHC0 _DSM requires 32-bit register accesses. Use the xHCI
            // defined low/high Dword pair rather than a native 64-bit MMIO store.
            Write32(address, (uint)value);
            Write32(address + 4, (uint)(value >> 32));
            Rpi4PageTableNative.DsbIsb();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static byte Read8(ulong address) => *(byte*)address;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ushort Read16(ulong address) => *(ushort*)address;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static uint Read32(ulong address) => *(uint*)address;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Write16(ulong address, ushort value) => *(ushort*)address = value;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Write32(ulong address, uint value) => *(uint*)address = value;

        private static string Hex(byte value) => "0x" + value.ToString("X2");
        private static string Hex(ushort value) => "0x" + value.ToString("X4");
        private static string Hex(uint value) => "0x" + value.ToString("X8");
        private static string Hex(ulong value) => "0x" + value.ToString("X");

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Trb
        {
            public ulong Parameter;
            public uint Status;
            public uint Control;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct ErstEntry
        {
            public ulong SegmentBase;
            public uint SegmentSize;
            public uint Reserved;
        }

        private sealed class UsbDevice
        {
            public byte SlotId;
            public uint RouteString;
            public byte RootPort;
            public byte Speed;
            public byte TtHubSlot;
            public byte TtPort;
            public byte TtThinkTime;
            public ushort MaxPacket0;
            public byte* DeviceContext;
            public byte* InputContext;
            public ProducerRing Ep0Ring = null!;

            public byte DeviceClass;
            public byte DeviceProtocol;
            public ushort VendorId;
            public ushort ProductId;
            public byte ConfigurationValue;

            public bool IsHub;
            public byte HubPortCount;
            public byte HubTtThinkTime;
            public bool HubMultiTt;

            public bool IsKeyboard;
            public byte KeyboardInterface;
            public byte KeyboardEndpointAddress;
            public ushort KeyboardEndpointMaxPacket;
            public byte KeyboardEndpointInterval;
        }

        private sealed class ProducerRing
        {
            private readonly Rpi4DmaArena _arena;
            private readonly Trb* _trbs;
            private readonly int _count;
            private int _index;
            private uint _cycle;

            public ulong PhysicalBase { get; }

            public ProducerRing(Rpi4DmaArena arena, int count)
            {
                _arena = arena;
                _count = count;
                _trbs = (Trb*)arena.Alloc((nuint)(count * sizeof(Trb)), 64);
                if (_trbs == null)
                    throw new OutOfMemoryException("xHCI transfer ring allocation failed");

                PhysicalBase = arena.Physical(_trbs);
                _index = 0;
                _cycle = 1;
                WriteLinkTrb();
                Rpi4DmaArena.Clean(_trbs, (nuint)(count * sizeof(Trb)));
            }

            public ulong Enqueue(ulong parameter, uint status, uint control)
            {
                if (_index >= _count - 1)
                    throw new InvalidOperationException("xHCI producer index reached Link TRB");

                Trb* trb = &_trbs[_index];
                trb->Parameter = parameter;
                trb->Status = status;
                trb->Control = control | _cycle;
                Rpi4DmaArena.Clean(trb, (nuint)sizeof(Trb));

                ulong physical = PhysicalBase + (ulong)(_index * sizeof(Trb));
                _index++;
                if (_index == _count - 1)
                {
                    WriteLinkTrb();
                    _index = 0;
                    _cycle ^= 1U;
                }

                return physical;
            }

            private void WriteLinkTrb()
            {
                Trb* link = &_trbs[_count - 1];
                link->Parameter = PhysicalBase;
                link->Status = 0;
                link->Control = (TrbLink << TrbTypeShift) | TrbToggleCycle | _cycle;
                Rpi4DmaArena.Clean(link, (nuint)sizeof(Trb));
            }
        }
    }
}
