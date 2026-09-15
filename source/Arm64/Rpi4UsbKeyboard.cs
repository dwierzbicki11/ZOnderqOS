using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.ARM64.Cpu;

namespace ZonderqOS
{
    /// <summary>
    /// Minimal physical Raspberry Pi 4 USB keyboard stack.
    ///
    /// Scope is intentionally narrow: the onboard VL805 xHCI controller, direct
    /// root-port USB HID boot keyboards, and polling (no xHCI MSI/IRQ yet).
    /// CosmosEnableKeyboard stays false because the generic ARM64 keyboard path
    /// currently targets QEMU virtio rather than the Pi's VL805.
    /// </summary>
    internal static unsafe class Rpi4UsbKeyboard
    {
        private const ulong PcieRegPhysicalBase = 0xFD500000UL;
        private const ulong PcieCpuMmioWindow = 0x600000000UL;
        private const ulong PcieBusMmioBase = 0xF8000000UL;
        private const ulong PcieBusMmioLength = 0x04000000UL;
        private const ulong PcieExtCfgDataOffset = 0x8000UL;
        private const ulong PcieExtCfgIndexOffset = 0x9000UL;
        private const ulong PcieStatusOffset = 0x4068UL;
        private const uint PcieLinkReadyMask = 0x30U;

        private const ulong PciVendorIdOffset = 0x00UL;
        private const ulong PciCommandOffset = 0x04UL;
        private const ulong PciProgIfOffset = 0x09UL;
        private const ulong PciSubclassOffset = 0x0AUL;
        private const ulong PciClassOffset = 0x0BUL;
        private const ulong PciBar0Offset = 0x10UL;
        private const ulong PciSecondaryBusOffset = 0x19UL;
        private const ushort PciCommandMemorySpace = 0x0002;
        private const ushort PciCommandBusMaster = 0x0004;

        private const ulong CapHcsParams1 = 0x04UL;
        private const ulong CapHcsParams2 = 0x08UL;
        private const ulong CapHccParams1 = 0x10UL;
        private const ulong CapDoorbellOffset = 0x14UL;
        private const ulong CapRuntimeOffset = 0x18UL;

        private const ulong OpUsbCmd = 0x00UL;
        private const ulong OpUsbSts = 0x04UL;
        private const ulong OpPageSize = 0x08UL;
        private const ulong OpCrcr = 0x18UL;
        private const ulong OpDcbaap = 0x30UL;
        private const ulong OpConfig = 0x38UL;
        private const ulong OpPortBase = 0x400UL;
        private const ulong OpPortStride = 0x10UL;

        private const uint UsbCmdRun = 1U << 0;
        private const uint UsbCmdReset = 1U << 1;
        private const uint UsbStsHalted = 1U << 0;
        private const uint UsbStsHostError = 1U << 2;
        private const uint UsbStsControllerNotReady = 1U << 11;

        private const ulong RuntimeInterrupter0 = 0x20UL;
        private const ulong IrIman = 0x00UL;
        private const ulong IrImod = 0x04UL;
        private const ulong IrErstSz = 0x08UL;
        private const ulong IrErstBa = 0x10UL;
        private const ulong IrErDp = 0x18UL;
        private const ulong ErdpEventHandlerBusy = 1UL << 3;

        private const uint PortConnect = 1U << 0;
        private const uint PortEnabled = 1U << 1;
        private const uint PortReset = 1U << 4;
        private const uint PortPower = 1U << 9;
        private const uint PortWarmReset = 1U << 31;
        private const uint PortChangeMask = 0x00FE0000U;
        private const uint PortReadOnlyMask = (1U << 0) | (1U << 3) | (0xFU << 10) | (1U << 30);
        private const uint PortReadWriteStateMask = (0xFU << 5) | (1U << 9) | (0x3U << 14) | (0x7U << 25);

        private const int TrbSize = 16;
        private const int RingBytes = 4096;
        private const int RingTrbCount = RingBytes / TrbSize;
        private const int RingUsableTrbs = RingTrbCount - 1;

        private const uint TrbCycle = 1U << 0;
        private const uint TrbToggleCycle = 1U << 1;
        private const uint TrbIsp = 1U << 2;
        private const uint TrbIoc = 1U << 5;
        private const uint TrbIdt = 1U << 6;
        private const uint TrbDirIn = 1U << 16;

        private const uint TrbNormal = 1;
        private const uint TrbSetup = 2;
        private const uint TrbData = 3;
        private const uint TrbStatus = 4;
        private const uint TrbLink = 6;
        private const uint TrbEnableSlot = 9;
        private const uint TrbDisableSlot = 10;
        private const uint TrbAddressDevice = 11;
        private const uint TrbConfigureEndpoint = 12;
        private const uint TrbEvaluateContext = 13;
        private const uint TrbTransferEvent = 32;
        private const uint TrbCommandCompletionEvent = 33;
        private const uint TrbPortStatusEvent = 34;

        private const byte CompletionSuccess = 1;
        private const byte CompletionShortPacket = 13;

        private const uint UsbRequestGetDescriptor = 6;
        private const uint UsbRequestSetConfiguration = 9;
        private const uint HidRequestSetIdle = 0x0A;
        private const uint HidRequestSetProtocol = 0x0B;
        private const byte DescriptorDevice = 1;
        private const byte DescriptorConfiguration = 2;
        private const byte DescriptorInterface = 4;
        private const byte DescriptorEndpoint = 5;

        private const ulong DmaPhysicalLimit = 0xC0000000UL;

        private const ulong DescriptorValid = 1UL << 0;
        private const ulong DescriptorTable = 1UL << 1;
        private const ulong AddressMask = 0x0000FFFFFFFFF000UL;

        private static ulong _pcieRegs;
        private static ulong _vl805Config;
        private static ulong _xhciBase;
        private static ulong _opBase;
        private static ulong _doorbellBase;
        private static ulong _runtimeBase;
        private static ulong _interrupter0;
        private static uint _maxSlots;
        private static uint _maxPorts;
        private static uint _contextSize;

        private static DmaArena _dma;
        private static DmaBlock _dcbaa;
        private static DmaBlock _erst;
        private static DmaBlock _eventRing;
        private static DmaBlock _inputContext;
        private static DmaBlock _outputContext;
        private static DmaBlock _deviceDescriptor;
        private static DmaBlock _configDescriptor;
        private static DmaBlock _keyboardReport;
        private static Ring _commandRing;
        private static Ring _ep0Ring;
        private static Ring _keyboardRing;

        private static int _eventIndex;
        private static uint _eventCycle = 1;
        private static byte _slotId;
        private static byte _rootPort;
        private static byte _deviceSpeed;
        private static ushort _ep0MaxPacket;
        private static byte _keyboardInterface;
        private static byte _keyboardEndpointAddress;
        private static byte _keyboardEndpointId;
        private static ushort _keyboardMaxPacket;
        private static byte _keyboardMaxBurst;
        private static byte _keyboardInterval;
        private static ulong _keyboardTransferTrb;
        private static uint _attemptedPorts;
        private static ulong _nextAttachPoll;
        private static bool _verboseAttach;

        private static readonly byte[] _previousReport = new byte[8];
        private static readonly char[] _keyQueue = new char[32];
        private static int _keyQueueHead;
        private static int _keyQueueTail;
        private static bool _capsLock;

        internal static bool ControllerReady { get; private set; }
        internal static bool KeyboardReady { get; private set; }
        internal static string LastError { get; private set; } = string.Empty;

        internal static bool Initialize()
        {
            if (ControllerReady)
                return true;

            LastError = string.Empty;
            KeyboardReady = false;
            _attemptedPorts = 0;

            Console.WriteLine("RPi4 USB keyboard: initializing VL805/xHCI polling driver...");

            if (!DiscoverController())
                return Fail("VL805 discovery failed: " + LastError);

            if (!_dma.Initialize())
                return Fail("DMA arena unavailable: " + _dma.Error);

            Console.WriteLine("  DMA arena: " + Hex(_dma.PhysicalBase) + " .. " +
                              Hex(_dma.PhysicalBase + _dma.Size - 1));

            if (!ClaimLegacyOwnership())
                return Fail("xHCI legacy ownership handoff failed");

            if (!ResetController())
                return Fail("xHCI reset failed: " + LastError);

            if (!AllocateControllerStructures())
                return Fail("xHCI DMA structure allocation failed: " + LastError);

            if (!ProgramController())
                return Fail("xHCI controller programming failed: " + LastError);

            if (!StartController())
                return Fail("xHCI controller did not enter running state");

            ControllerReady = true;
            Console.WriteLine("  xHCI controller: RUNNING (polling event ring)");

            _verboseAttach = true;
            TryAttachKeyboard();
            _verboseAttach = false;
            ScheduleNextAttachPoll();
            return true;
        }

        internal static void PollAttach()
        {
            if (!ControllerReady || KeyboardReady)
                return;

            ulong now = Rpi4UsbNative.TimerCounter();
            if (_nextAttachPoll != 0 && now < _nextAttachPoll)
                return;

            for (uint port = 1; port <= _maxPorts && port <= 32; port++)
            {
                uint value = Read32(PortAddress(port));
                if ((value & PortConnect) == 0)
                    _attemptedPorts &= ~(1U << (int)(port - 1));
            }

            TryAttachKeyboard();
            ScheduleNextAttachPoll();
        }

        internal static bool TryReadKey(out char key)
        {
            key = '\0';

            if (TryDequeueKey(out key))
                return true;

            if (!KeyboardReady)
            {
                PollAttach();
                return false;
            }

            int guard = 32;
            while (guard-- > 0 && TryPopEvent(out Trb evt))
            {
                uint type = TrbType(evt.Control);

                if (type == TrbTransferEvent)
                {
                    byte slot = (byte)(evt.Control >> 24);
                    byte endpointId = (byte)((evt.Control >> 16) & 0x1FU);
                    ulong trbPointer = evt.Parameter & ~0xFUL;
                    byte completion = CompletionCode(evt);

                    if (slot == _slotId &&
                        endpointId == _keyboardEndpointId &&
                        trbPointer == (_keyboardTransferTrb & ~0xFUL))
                    {
                        if (completion == CompletionSuccess || completion == CompletionShortPacket)
                        {
                            Rpi4UsbNative.DmaInvalidate((nint)_keyboardReport.Pointer, (ulong)_keyboardReport.Size);
                            ProcessKeyboardReport();
                            SubmitKeyboardTransfer();

                            if (TryDequeueKey(out key))
                                return true;
                        }
                        else
                        {
                            KeyboardReady = false;
                            LastError = "keyboard transfer completion code " + completion;
                            byte disconnectedPort = _rootPort;
                            CleanupSlot();
                            _attemptedPorts &= ~(1U << (disconnectedPort - 1));
                            ScheduleNextAttachPoll();
                            return false;
                        }
                    }
                }
            }

            return false;
        }

        private static bool DiscoverController()
        {
            if (Limine.HHDM.Response == null)
            {
                LastError = "Limine HHDM response missing";
                return false;
            }

            DeviceMapper.EnsureMapped(PcieRegPhysicalBase);
            if (!VerifyDeviceMapping(PcieRegPhysicalBase))
            {
                LastError = "BCM2711 PCI registers are not Device-mapped; run the validated probe first";
                return false;
            }

            ulong hhdm = Limine.HHDM.Response->Offset;
            _pcieRegs = PcieRegPhysicalBase + hhdm;

            uint link = Read32(_pcieRegs + PcieStatusOffset);
            byte secondaryBus = Read8(_pcieRegs + PciSecondaryBusOffset);
            if ((link & PcieLinkReadyMask) != PcieLinkReadyMask || secondaryBus == 0)
            {
                LastError = "BCM2711 PCIe link/secondary bus is not ready";
                return false;
            }

            _vl805Config = SelectConfigFunction(secondaryBus, 0, 0);
            ushort vendor = Read16(_vl805Config + PciVendorIdOffset);
            byte classCode = Read8(_vl805Config + PciClassOffset);
            byte subclass = Read8(_vl805Config + PciSubclassOffset);
            byte progIf = Read8(_vl805Config + PciProgIfOffset);

            if (vendor == 0 || vendor == 0xFFFF ||
                classCode != 0x0C || subclass != 0x03 || progIf != 0x30)
            {
                LastError = "downstream PCI function is not the VL805 xHCI controller";
                return false;
            }

            uint barLow = Read32(_vl805Config + PciBar0Offset);
            if ((barLow & 1U) != 0)
            {
                LastError = "VL805 BAR0 is I/O space";
                return false;
            }

            uint barType = (barLow >> 1) & 3U;
            if (barType == 3U)
            {
                LastError = "VL805 BAR0 has reserved memory type";
                return false;
            }

            ulong pciBar = barLow & 0xFFFFFFF0U;
            if (barType == 2U)
                pciBar |= (ulong)Read32(_vl805Config + PciBar0Offset + 4) << 32;

            if (pciBar < PcieBusMmioBase || pciBar >= PcieBusMmioBase + PcieBusMmioLength)
            {
                LastError = "VL805 BAR0 is outside the PFTF PCI aperture";
                return false;
            }

            ushort command = Read16(_vl805Config + PciCommandOffset);
            command |= PciCommandMemorySpace | PciCommandBusMaster;
            Write16(_vl805Config + PciCommandOffset, command);
            Rpi4UsbNative.DsbIsb();

            ushort commandAfter = Read16(_vl805Config + PciCommandOffset);
            if ((commandAfter & (PciCommandMemorySpace | PciCommandBusMaster)) !=
                (PciCommandMemorySpace | PciCommandBusMaster))
            {
                LastError = "could not enable PCI Memory Space + Bus Master";
                return false;
            }

            ulong xhciPhysical = PcieCpuMmioWindow + (pciBar - PcieBusMmioBase);
            DeviceMapper.EnsureMapped(xhciPhysical);
            if (!VerifyDeviceMapping(xhciPhysical))
            {
                LastError = "VL805 MMIO is not Device-mapped";
                return false;
            }

            _xhciBase = xhciPhysical + hhdm;

            uint cap0 = Read32(_xhciBase);
            byte capLength = (byte)(cap0 & 0xFFU);
            ushort version = (ushort)(cap0 >> 16);
            uint hcs1 = Read32(_xhciBase + CapHcsParams1);
            uint hcc1 = Read32(_xhciBase + CapHccParams1);

            _maxSlots = hcs1 & 0xFFU;
            _maxPorts = (hcs1 >> 24) & 0xFFU;
            _contextSize = (hcc1 & (1U << 2)) != 0 ? 64U : 32U;

            if (capLength < 0x20 || capLength > 0x80 ||
                version < 0x0090 || version > 0x0200 ||
                _maxSlots == 0 || _maxPorts == 0 || _maxPorts > 32)
            {
                LastError = "xHCI capability block failed sanity checks";
                return false;
            }

            _opBase = _xhciBase + capLength;
            _doorbellBase = _xhciBase + (Read32(_xhciBase + CapDoorbellOffset) & 0xFFFFFFFCU);
            _runtimeBase = _xhciBase + (Read32(_xhciBase + CapRuntimeOffset) & 0xFFFFFFE0U);
            _interrupter0 = _runtimeBase + RuntimeInterrupter0;

            Console.WriteLine("  VL805 PCI bus master: ON");
            Console.WriteLine("  xHCI: v" + Hex(version) +
                              ", ports=" + _maxPorts +
                              ", slots=" + _maxSlots +
                              ", context=" + _contextSize + " bytes");
            return true;
        }

        private static bool ClaimLegacyOwnership()
        {
            uint hcc1 = Read32(_xhciBase + CapHccParams1);
            uint offsetDwords = (hcc1 >> 16) & 0xFFFFU;
            int guard = 64;

            while (offsetDwords != 0 && guard-- > 0)
            {
                ulong address = _xhciBase + ((ulong)offsetDwords << 2);
                uint header = Read32(address);
                byte id = (byte)(header & 0xFFU);
                byte next = (byte)((header >> 8) & 0xFFU);

                if (id == 1)
                {
                    if ((header & (1U << 24)) == 0)
                        Write32(address, header | (1U << 24));

                    if (!WaitRegister(address, 1U << 16, 0, 1000))
                    {
                        LastError = "firmware BIOS-owned semaphore did not clear";
                        return false;
                    }

                    Write32(address + 4, 0);
                    Rpi4UsbNative.DsbIsb();
                    Console.WriteLine("  xHCI legacy ownership: OS");
                    return true;
                }

                if (next == 0)
                    break;

                offsetDwords += next;
            }

            Console.WriteLine("  xHCI legacy ownership: no legacy capability (OK)");
            return true;
        }

        private static bool ResetController()
        {
            uint command = Read32(_opBase + OpUsbCmd);
            if ((command & UsbCmdRun) != 0)
            {
                Write32(_opBase + OpUsbCmd, command & ~UsbCmdRun);
                if (!WaitRegister(_opBase + OpUsbSts, UsbStsHalted, UsbStsHalted, 1000))
                {
                    LastError = "controller did not halt";
                    return false;
                }
            }

            Write32(_opBase + OpUsbCmd, Read32(_opBase + OpUsbCmd) | UsbCmdReset);
            if (!WaitRegister(_opBase + OpUsbCmd, UsbCmdReset, 0, 1000))
            {
                LastError = "HCRST did not clear";
                return false;
            }

            if (!WaitRegister(_opBase + OpUsbSts, UsbStsControllerNotReady, 0, 1000))
            {
                LastError = "CNR did not clear";
                return false;
            }

            if ((Read32(_opBase + OpPageSize) & 1U) == 0)
            {
                LastError = "controller does not advertise 4 KiB pages";
                return false;
            }

            if ((Read32(_opBase + OpUsbSts) & UsbStsHostError) != 0)
            {
                LastError = "Host Controller Error after reset";
                return false;
            }

            Console.WriteLine("  xHCI reset: OK");
            return true;
        }

        private static bool AllocateControllerStructures()
        {
            _dcbaa = _dma.Allocate(256 * 8, 64);
            _erst = _dma.Allocate(64, 64);
            _eventRing = _dma.Allocate(RingBytes, 4096);
            _inputContext = _dma.Allocate((int)(_contextSize * 33U), 64);
            _outputContext = _dma.Allocate((int)(_contextSize * 32U), 64);
            _deviceDescriptor = _dma.Allocate(64, 64);
            _configDescriptor = _dma.Allocate(4096, 64);
            _keyboardReport = _dma.Allocate(64, 64);

            if (!_dcbaa.Valid || !_erst.Valid || !_eventRing.Valid ||
                !_inputContext.Valid || !_outputContext.Valid ||
                !_deviceDescriptor.Valid || !_configDescriptor.Valid || !_keyboardReport.Valid)
            {
                LastError = _dma.Error;
                return false;
            }

            if (!InitRing(ref _commandRing) ||
                !InitRing(ref _ep0Ring) ||
                !InitRing(ref _keyboardRing))
            {
                LastError = _dma.Error;
                return false;
            }

            uint hcs2 = Read32(_xhciBase + CapHcsParams2);
            uint scratchHi = (hcs2 >> 21) & 0x1FU;
            uint scratchLo = (hcs2 >> 27) & 0x1FU;
            uint scratchCount = (scratchHi << 5) | scratchLo;

            if (scratchCount != 0)
            {
                DmaBlock pointers = _dma.Allocate((int)(scratchCount * 8U), 64);
                if (!pointers.Valid)
                {
                    LastError = "scratchpad pointer array: " + _dma.Error;
                    return false;
                }

                for (uint i = 0; i < scratchCount; i++)
                {
                    DmaBlock page = _dma.Allocate(4096, 4096);
                    if (!page.Valid)
                    {
                        LastError = "scratchpad " + i + ": " + _dma.Error;
                        return false;
                    }

                    WriteDma64((byte*)((ulong)pointers.Pointer + ((ulong)i * 8UL)), page.Physical);
                    Rpi4UsbNative.DmaCleanInvalidate((nint)page.Pointer, 4096);
                }

                Rpi4UsbNative.DmaClean((nint)pointers.Pointer, (ulong)pointers.Size);
                WriteDma64(_dcbaa.Pointer, pointers.Physical);
                Console.WriteLine("  scratchpads: " + scratchCount);
            }
            else
            {
                Console.WriteLine("  scratchpads: 0");
            }

            Rpi4UsbNative.DmaClean((nint)_dcbaa.Pointer, (ulong)_dcbaa.Size);
            Rpi4UsbNative.DmaCleanInvalidate((nint)_eventRing.Pointer, (ulong)_eventRing.Size);
            return true;
        }

        private static bool ProgramController()
        {
            uint slotsToEnable = _maxSlots > 32 ? 32U : _maxSlots;

            Write64Mmio(_opBase + OpCrcr, _commandRing.Block.Physical | 1UL);
            Write64Mmio(_opBase + OpDcbaap, _dcbaa.Physical);
            Write32(_opBase + OpConfig, slotsToEnable);

            WriteDma64(_erst.Pointer + 0, _eventRing.Physical);
            WriteDma32(_erst.Pointer + 8, RingTrbCount);
            WriteDma32(_erst.Pointer + 12, 0);
            Rpi4UsbNative.DmaClean((nint)_erst.Pointer, 16);

            Write32(_interrupter0 + IrErstSz, 1);
            Write64Mmio(_interrupter0 + IrErstBa, _erst.Physical);
            _eventIndex = 0;
            _eventCycle = 1;
            Write64Mmio(_interrupter0 + IrErDp, _eventRing.Physical);
            Write32(_interrupter0 + IrImod, 0);
            Write32(_interrupter0 + IrIman, 1);

            Rpi4UsbNative.DsbIsb();
            return true;
        }

        private static bool StartController()
        {
            Write32(_opBase + OpUsbCmd, Read32(_opBase + OpUsbCmd) | UsbCmdRun);
            if (!WaitRegister(_opBase + OpUsbSts, UsbStsHalted, 0, 1000))
                return false;

            for (uint port = 1; port <= _maxPorts; port++)
            {
                ulong address = PortAddress(port);
                uint value = Read32(address);
                if ((value & PortPower) == 0)
                    Write32(address, PortNeutral(value) | PortPower);
            }

            DelayMilliseconds(20);
            return (Read32(_opBase + OpUsbSts) & UsbStsHostError) == 0;
        }

        private static bool TryAttachKeyboard()
        {
            if (!ControllerReady && _opBase == 0)
                return false;

            for (uint port = 1; port <= _maxPorts && port <= 32; port++)
            {
                uint bit = 1U << (int)(port - 1);
                uint portSc = Read32(PortAddress(port));

                if ((portSc & PortConnect) == 0 || (_attemptedPorts & bit) != 0)
                    continue;

                _attemptedPorts |= bit;

                AttachLog("  USB: device connected on root port " + port);

                if (TryEnumerateKeyboard((byte)port))
                {
                    KeyboardReady = true;
                    AttachLog("  USB HID boot keyboard: READY on root port " + port);
                    return true;
                }

                AttachLog("  USB: port " + port + " is not a supported direct boot keyboard (" +
                          LastError + ")");
                CleanupSlot();
            }

            return false;
        }

        private static bool TryEnumerateKeyboard(byte port)
        {
            LastError = string.Empty;
            _rootPort = port;
            _slotId = 0;

            if (!ResetPort(port, out byte speed))
                return false;

            _deviceSpeed = speed;
            if (speed == 4)
            {
                LastError = "SuperSpeed HID companion descriptors are not enabled yet";
                return false;
            }

            if (speed < 1 || speed > 3)
            {
                LastError = "unsupported/unknown USB speed " + speed;
                return false;
            }

            ResetProducerRing(ref _ep0Ring);

            if (!EnableSlot(out _slotId))
                return false;

            ZeroBlock(_outputContext);
            WriteDcbaaSlot(_slotId, _outputContext.Physical);

            _ep0MaxPacket = speed == 2 ? (ushort)8 : (ushort)64;

            if (!PrepareAddressContext(port, speed, _ep0MaxPacket))
                return false;

            if (!SendCommand(_inputContext.Physical, TrbAddressDevice, _slotId, out _))
            {
                LastError = "Address Device: " + LastError;
                return false;
            }

            Rpi4UsbNative.DmaInvalidate((nint)_outputContext.Pointer, (ulong)_outputContext.Size);

            if (!GetDeviceDescriptor(out byte configurationCount))
                return false;

            if (!FindBootKeyboard(configurationCount, out byte configurationValue))
                return false;

            if (!ControlNoData(0x00, UsbRequestSetConfiguration, configurationValue, 0))
            {
                LastError = "SET_CONFIGURATION failed: " + LastError;
                return false;
            }

            if (!ConfigureKeyboardEndpoint())
                return false;

            if (!ControlNoData(0x21, HidRequestSetProtocol, 0, _keyboardInterface))
            {
                LastError = "HID SET_PROTOCOL(boot) failed: " + LastError;
                return false;
            }

            ControlNoData(0x21, HidRequestSetIdle, 0, _keyboardInterface);

            ClearPreviousReport();
            if (!SubmitKeyboardTransfer())
                return false;

            return true;
        }

        private static bool ResetPort(byte port, out byte speed)
        {
            speed = 0;
            ulong address = PortAddress(port);
            uint value = Read32(address);

            if ((value & PortConnect) == 0)
            {
                LastError = "port disconnected";
                return false;
            }

            byte initialSpeed = (byte)((value >> 10) & 0xFU);
            uint resetBit = initialSpeed >= 4 ? PortWarmReset : PortReset;

            Write32(address, PortNeutral(value) | resetBit);

            ulong deadline = DeadlineMilliseconds(1000);
            while (true)
            {
                value = Read32(address);
                if ((value & resetBit) == 0 && (value & PortEnabled) != 0)
                    break;

                if (DeadlinePassed(deadline))
                {
                    LastError = "port reset timed out; PORTSC=" + Hex(value);
                    return false;
                }
            }

            Write32(address, PortNeutral(value) | (value & PortChangeMask));
            Rpi4UsbNative.DsbIsb();

            value = Read32(address);
            speed = (byte)((value >> 10) & 0xFU);
            AttachLog("    port reset: OK, speed ID=" + speed + ", PORTSC=" + Hex(value));
            return true;
        }

        private static bool EnableSlot(out byte slot)
        {
            slot = 0;
            if (!SendCommand(0, TrbEnableSlot, 0, out byte returnedSlot))
            {
                LastError = "Enable Slot: " + LastError;
                return false;
            }

            if (returnedSlot == 0)
            {
                LastError = "Enable Slot returned slot 0";
                return false;
            }

            slot = returnedSlot;
            return true;
        }

        private static bool PrepareAddressContext(byte port, byte speed, ushort maxPacket)
        {
            ZeroBlock(_inputContext);

            uint* control = (uint*)_inputContext.Pointer;
            control[1] = 0x3U;

            byte* slot = _inputContext.Pointer + (int)_contextSize;
            WriteContext32(slot, 0, ((uint)speed << 20) | (1U << 27));
            WriteContext32(slot, 1, (uint)port << 16);

            byte* ep0 = _inputContext.Pointer + (int)(_contextSize * 2U);
            WriteContext32(ep0, 0, 0);
            WriteContext32(ep0, 1, (3U << 1) | (4U << 3) | ((uint)maxPacket << 16));
            WriteContext64(ep0, 2, _ep0Ring.Block.Physical | 1UL);
            WriteContext32(ep0, 4, 8U);

            Rpi4UsbNative.DmaClean((nint)_inputContext.Pointer, (ulong)_inputContext.Size);
            Rpi4UsbNative.DmaCleanInvalidate((nint)_outputContext.Pointer, (ulong)_outputContext.Size);
            return true;
        }

        private static bool GetDeviceDescriptor(out byte configurationCount)
        {
            configurationCount = 0;
            ZeroBlock(_deviceDescriptor);

            if (!ControlIn(0x80, UsbRequestGetDescriptor,
                           (ushort)(DescriptorDevice << 8), 0, 18, _deviceDescriptor))
            {
                LastError = "GET_DESCRIPTOR(Device): " + LastError;
                return false;
            }

            byte* d = _deviceDescriptor.Pointer;
            if (d[0] < 18 || d[1] != DescriptorDevice)
            {
                LastError = "invalid device descriptor";
                return false;
            }

            ushort actualMps;
            byte mps0 = d[7];
            if (_deviceSpeed == 4)
                actualMps = mps0 < 16 ? (ushort)(1U << mps0) : (ushort)0;
            else
                actualMps = mps0;

            if (actualMps == 0 || actualMps > 512)
            {
                LastError = "invalid EP0 max packet " + actualMps;
                return false;
            }

            configurationCount = d[17];
            if (configurationCount == 0)
            {
                LastError = "device exposes zero configurations";
                return false;
            }

            if (actualMps != _ep0MaxPacket)
            {
                if (!UpdateEp0MaxPacket(actualMps))
                    return false;
                _ep0MaxPacket = actualMps;
            }

            AttachLog("    USB device descriptor: VID=" +
                      Hex(ReadLe16(d + 8)) + " PID=" + Hex(ReadLe16(d + 10)) +
                      " EP0=" + _ep0MaxPacket);
            return true;
        }

        private static bool UpdateEp0MaxPacket(ushort maxPacket)
        {
            Rpi4UsbNative.DmaInvalidate((nint)_outputContext.Pointer, (ulong)_outputContext.Size);
            ZeroBlock(_inputContext);

            uint* control = (uint*)_inputContext.Pointer;
            control[1] = 1U << 1;

            byte* outputEp0 = _outputContext.Pointer + (int)_contextSize;
            byte* inputEp0 = _inputContext.Pointer + (int)(_contextSize * 2U);
            CopyBytes(inputEp0, outputEp0, (int)_contextSize);

            uint epInfo2 = ReadContext32(inputEp0, 1);
            epInfo2 &= 0x0000FFFFU;
            epInfo2 |= (uint)maxPacket << 16;
            WriteContext32(inputEp0, 1, epInfo2);

            Rpi4UsbNative.DmaClean((nint)_inputContext.Pointer, (ulong)_inputContext.Size);

            if (!SendCommand(_inputContext.Physical, TrbEvaluateContext, _slotId, out _))
            {
                LastError = "Evaluate Context for EP0 MPS: " + LastError;
                return false;
            }

            return true;
        }

        private static bool FindBootKeyboard(byte configurationCount, out byte configurationValue)
        {
            configurationValue = 0;

            for (byte configIndex = 0; configIndex < configurationCount; configIndex++)
            {
                ZeroBlock(_configDescriptor);

                ushort descriptorValue = (ushort)((DescriptorConfiguration << 8) | configIndex);
                if (!ControlIn(0x80, UsbRequestGetDescriptor, descriptorValue, 0, 9, _configDescriptor))
                    continue;

                byte* cfg = _configDescriptor.Pointer;
                if (cfg[0] < 9 || cfg[1] != DescriptorConfiguration)
                    continue;

                ushort totalLength = ReadLe16(cfg + 2);
                if (totalLength < 9 || totalLength > _configDescriptor.Size)
                {
                    LastError = "configuration descriptor length " + totalLength +
                                " exceeds " + _configDescriptor.Size;
                    continue;
                }

                if (!ControlIn(0x80, UsbRequestGetDescriptor, descriptorValue, 0,
                               totalLength, _configDescriptor))
                    continue;

                if (ParseBootKeyboardConfiguration(totalLength,
                                                   out byte iface,
                                                   out byte endpointAddress,
                                                   out ushort maxPacket,
                                                   out byte maxBurst,
                                                   out byte interval))
                {
                    configurationValue = cfg[5];
                    _keyboardInterface = iface;
                    _keyboardEndpointAddress = endpointAddress;
                    _keyboardEndpointId = (byte)(((endpointAddress & 0x0F) * 2) + 1);
                    _keyboardMaxPacket = maxPacket;
                    _keyboardMaxBurst = maxBurst;
                    _keyboardInterval = interval;

                    if (_keyboardEndpointId == 0 || _keyboardEndpointId > 31)
                    {
                        LastError = "invalid xHCI endpoint context index";
                        return false;
                    }

                    if (_keyboardMaxPacket < 8 || _keyboardMaxPacket > _keyboardReport.Size)
                    {
                        LastError = "keyboard interrupt packet " + _keyboardMaxPacket +
                                    " exceeds polling buffer";
                        return false;
                    }

                    AttachLog("    HID boot keyboard: interface=" + iface +
                              " endpoint=0x" + endpointAddress.ToString("X2") +
                              " maxPacket=" + maxPacket +
                              " burst=" + maxBurst +
                              " interval=" + interval);
                    return true;
                }
            }

            LastError = "no HID boot-keyboard interface on direct device";
            return false;
        }

        private static bool ParseBootKeyboardConfiguration(
            int totalLength,
            out byte interfaceNumber,
            out byte endpointAddress,
            out ushort maxPacket,
            out byte maxBurst,
            out byte interval)
        {
            interfaceNumber = 0;
            endpointAddress = 0;
            maxPacket = 0;
            maxBurst = 0;
            interval = 0;

            bool inBootKeyboard = false;
            int offset = 0;

            while (offset + 2 <= totalLength)
            {
                byte length = _configDescriptor.Pointer[offset];
                byte type = _configDescriptor.Pointer[offset + 1];

                if (length < 2 || offset + length > totalLength)
                    break;

                if (type == DescriptorInterface && length >= 9)
                {
                    byte* d = _configDescriptor.Pointer + offset;
                    inBootKeyboard =
                        d[3] == 0 &&
                        d[5] == 3 &&
                        d[6] == 1 &&
                        d[7] == 1;

                    if (inBootKeyboard)
                        interfaceNumber = d[2];
                }
                else if (type == DescriptorEndpoint && length >= 7 && inBootKeyboard)
                {
                    byte* d = _configDescriptor.Pointer + offset;
                    byte address = d[2];
                    byte attributes = d[3];

                    if ((address & 0x80) != 0 && (attributes & 0x03) == 0x03)
                    {
                        endpointAddress = address;
                        ushort rawMaxPacket = ReadLe16(d + 4);
                        maxPacket = (ushort)(rawMaxPacket & 0x07FFU);
                        maxBurst = (byte)((rawMaxPacket >> 11) & 0x03U);
                        interval = d[6];
                        return maxPacket != 0 && interval != 0;
                    }
                }

                offset += length;
            }

            return false;
        }

        private static bool ConfigureKeyboardEndpoint()
        {
            Rpi4UsbNative.DmaInvalidate((nint)_outputContext.Pointer, (ulong)_outputContext.Size);
            ZeroBlock(_inputContext);
            ResetProducerRing(ref _keyboardRing);

            uint* control = (uint*)_inputContext.Pointer;
            control[1] = (1U << 0) | (1U << _keyboardEndpointId);

            byte* inputSlot = _inputContext.Pointer + (int)_contextSize;
            byte* outputSlot = _outputContext.Pointer;
            CopyBytes(inputSlot, outputSlot, (int)_contextSize);

            uint slotInfo = ReadContext32(inputSlot, 0);
            slotInfo &= ~(0x1FU << 27);
            slotInfo |= (uint)_keyboardEndpointId << 27;
            WriteContext32(inputSlot, 0, slotInfo);

            byte* ep = _inputContext.Pointer + (int)(_contextSize * (uint)(_keyboardEndpointId + 1));

            uint interval = ComputeXhciInterval(_deviceSpeed, _keyboardInterval);
            ushort rawPacket = _keyboardMaxPacket;
            uint maxBurst = _deviceSpeed == 3 ? _keyboardMaxBurst : 0U;
            uint maxEsitPayload = (uint)rawPacket * (maxBurst + 1U);

            WriteContext32(ep, 0, interval << 16);
            WriteContext32(ep, 1,
                           (3U << 1) |
                           (7U << 3) |
                           (maxBurst << 8) |
                           ((uint)rawPacket << 16));
            WriteContext64(ep, 2, _keyboardRing.Block.Physical | 1UL);
            WriteContext32(ep, 4, (uint)rawPacket | (maxEsitPayload << 16));

            Rpi4UsbNative.DmaClean((nint)_inputContext.Pointer, (ulong)_inputContext.Size);

            if (!SendCommand(_inputContext.Physical, TrbConfigureEndpoint, _slotId, out _))
            {
                LastError = "Configure Endpoint: " + LastError;
                return false;
            }

            return true;
        }

        private static uint ComputeXhciInterval(byte speed, byte descriptorInterval)
        {
            if (speed == 1 || speed == 2)
            {
                uint microframes = (uint)descriptorInterval * 8U;
                uint exponent = 0;
                while (exponent < 15 && (1U << (int)(exponent + 1)) <= microframes)
                    exponent++;

                if (exponent < 3)
                    exponent = 3;
                if (exponent > 10)
                    exponent = 10;
                return exponent;
            }

            uint hs = descriptorInterval == 0 ? 0U : (uint)descriptorInterval - 1U;
            return hs > 15 ? 15U : hs;
        }

        private static bool SubmitKeyboardTransfer()
        {
            ZeroBlock(_keyboardReport);
            Rpi4UsbNative.DmaCleanInvalidate((nint)_keyboardReport.Pointer, (ulong)_keyboardReport.Size);

            uint length = _keyboardMaxPacket;
            _keyboardTransferTrb = EnqueueTrb(
                ref _keyboardRing,
                _keyboardReport.Physical,
                length,
                TrbControl(TrbNormal) | TrbIoc | TrbIsp);

            Rpi4UsbNative.DsbIsb();
            Write32(_doorbellBase + ((ulong)_slotId * 4UL), _keyboardEndpointId);
            return true;
        }

        private static bool ControlIn(
            byte requestType,
            uint request,
            ushort value,
            ushort index,
            ushort length,
            DmaBlock data)
        {
            return ControlTransfer(requestType, (byte)request, value, index, length, true, data);
        }

        private static bool ControlNoData(
            byte requestType,
            uint request,
            ushort value,
            ushort index)
        {
            DmaBlock none = default;
            return ControlTransfer(requestType, (byte)request, value, index, 0, false, none);
        }

        private static bool ControlTransfer(
            byte requestType,
            byte request,
            ushort value,
            ushort index,
            ushort length,
            bool directionIn,
            DmaBlock data)
        {
            ulong setup =
                requestType |
                ((ulong)request << 8) |
                ((ulong)value << 16) |
                ((ulong)index << 32) |
                ((ulong)length << 48);

            uint transferType = length == 0 ? 0U : directionIn ? 3U : 2U;

            EnqueueTrb(ref _ep0Ring,
                       setup,
                       8,
                       TrbControl(TrbSetup) | TrbIdt | (transferType << 16));

            if (length != 0)
            {
                if (!data.Valid || length > data.Size)
                {
                    LastError = "control transfer DMA buffer is invalid";
                    return false;
                }

                if (directionIn)
                    Rpi4UsbNative.DmaCleanInvalidate((nint)data.Pointer, (ulong)data.Size);
                else
                    Rpi4UsbNative.DmaClean((nint)data.Pointer, length);

                EnqueueTrb(ref _ep0Ring,
                           data.Physical,
                           length,
                           TrbControl(TrbData) | TrbIsp | (directionIn ? TrbDirIn : 0));
            }

            bool statusDirectionIn = length == 0 || !directionIn;
            ulong statusTrb = EnqueueTrb(
                ref _ep0Ring,
                0,
                0,
                TrbControl(TrbStatus) | TrbIoc | (statusDirectionIn ? TrbDirIn : 0));

            Rpi4UsbNative.DsbIsb();
            Write32(_doorbellBase + ((ulong)_slotId * 4UL), 1U);

            if (!WaitTransfer(_slotId, 1, statusTrb))
                return false;

            if (length != 0 && directionIn)
                Rpi4UsbNative.DmaInvalidate((nint)data.Pointer, (ulong)data.Size);

            return true;
        }

        private static bool SendCommand(
            ulong parameter,
            uint type,
            byte slot,
            out byte returnedSlot)
        {
            returnedSlot = 0;

            uint control = TrbControl(type);
            if (slot != 0)
                control |= (uint)slot << 24;

            ulong commandTrb = EnqueueTrb(ref _commandRing, parameter, 0, control);

            Rpi4UsbNative.DsbIsb();
            Write32(_doorbellBase, 0);

            ulong deadline = DeadlineMilliseconds(2000);
            while (!DeadlinePassed(deadline))
            {
                if (!TryPopEvent(out Trb evt))
                    continue;

                uint eventType = TrbType(evt.Control);
                if (eventType == TrbCommandCompletionEvent)
                {
                    ulong completed = evt.Parameter & ~0xFUL;
                    if (completed != (commandTrb & ~0xFUL))
                        continue;

                    byte completion = CompletionCode(evt);
                    returnedSlot = (byte)(evt.Control >> 24);

                    if (completion != CompletionSuccess)
                    {
                        LastError = "completion code " + completion;
                        return false;
                    }

                    return true;
                }
            }

            LastError = "command timeout";
            return false;
        }

        private static bool WaitTransfer(byte slotId, byte endpointId, ulong terminalTrb)
        {
            ulong deadline = DeadlineMilliseconds(2000);

            while (!DeadlinePassed(deadline))
            {
                if (!TryPopEvent(out Trb evt))
                    continue;

                if (TrbType(evt.Control) != TrbTransferEvent)
                    continue;

                byte slot = (byte)(evt.Control >> 24);
                byte endpoint = (byte)((evt.Control >> 16) & 0x1FU);
                if (slot != slotId || endpoint != endpointId)
                    continue;

                byte completion = CompletionCode(evt);
                ulong trbPointer = evt.Parameter & ~0xFUL;

                if (trbPointer == (terminalTrb & ~0xFUL))
                {
                    if (completion == CompletionSuccess || completion == CompletionShortPacket)
                        return true;

                    LastError = "transfer completion code " + completion;
                    return false;
                }

                if (completion != CompletionSuccess && completion != CompletionShortPacket)
                {
                    LastError = "transfer stage completion code " + completion;
                    return false;
                }
            }

            LastError = "transfer timeout";
            return false;
        }

        private static bool TryPopEvent(out Trb evt)
        {
            evt = default;

            byte* pointer = _eventRing.Pointer + (_eventIndex * TrbSize);
            Rpi4UsbNative.DmaInvalidate((nint)pointer, TrbSize);

            Trb* source = (Trb*)pointer;
            uint control = source->Control;
            if ((control & TrbCycle) != _eventCycle)
                return false;

            evt.Parameter = source->Parameter;
            evt.Status = source->Status;
            evt.Control = control;

            _eventIndex++;
            if (_eventIndex >= RingTrbCount)
            {
                _eventIndex = 0;
                _eventCycle ^= 1U;
            }

            ulong next = _eventRing.Physical + ((ulong)_eventIndex * TrbSize);
            Write64Mmio(_interrupter0 + IrErDp, next | ErdpEventHandlerBusy);

            if ((Read32(_interrupter0 + IrIman) & 1U) != 0)
                Write32(_interrupter0 + IrIman, 1U);

            return true;
        }

        private static bool InitRing(ref Ring ring)
        {
            ring.Block = _dma.Allocate(RingBytes, 4096);
            if (!ring.Block.Valid)
                return false;

            ResetProducerRing(ref ring);
            return true;
        }

        private static void ResetProducerRing(ref Ring ring)
        {
            ZeroBlock(ring.Block);
            ring.Enqueue = 0;
            ring.Cycle = 1;
            WriteLinkTrb(ref ring, 1);
            Rpi4UsbNative.DmaClean((nint)ring.Block.Pointer, (ulong)ring.Block.Size);
        }

        private static ulong EnqueueTrb(
            ref Ring ring,
            ulong parameter,
            uint status,
            uint control)
        {
            if (ring.Enqueue >= RingUsableTrbs)
                WrapProducerRing(ref ring);

            int index = ring.Enqueue;
            Trb* trb = (Trb*)(ring.Block.Pointer + (index * TrbSize));

            trb->Parameter = parameter;
            trb->Status = status;
            trb->Control = control | ring.Cycle;

            Rpi4UsbNative.DmaClean((nint)trb, TrbSize);

            ulong physical = ring.Block.Physical + ((ulong)index * TrbSize);
            ring.Enqueue++;

            if (ring.Enqueue >= RingUsableTrbs)
                WrapProducerRing(ref ring);

            return physical;
        }

        private static void WrapProducerRing(ref Ring ring)
        {
            WriteLinkTrb(ref ring, ring.Cycle);
            ring.Enqueue = 0;
            ring.Cycle ^= 1U;
        }

        private static void WriteLinkTrb(ref Ring ring, uint cycle)
        {
            Trb* link = (Trb*)(ring.Block.Pointer + (RingUsableTrbs * TrbSize));
            link->Parameter = ring.Block.Physical;
            link->Status = 0;
            link->Control = TrbControl(TrbLink) | TrbToggleCycle | cycle;
            Rpi4UsbNative.DmaClean((nint)link, TrbSize);
        }

        private static void WriteDcbaaSlot(byte slot, ulong physical)
        {
            byte* entry = _dcbaa.Pointer + (slot * 8);
            WriteDma64(entry, physical);
            Rpi4UsbNative.DmaClean((nint)entry, 8);
        }

        private static void CleanupSlot()
        {
            if (_slotId != 0)
            {
                SendCommand(0, TrbDisableSlot, _slotId, out _);
                WriteDcbaaSlot(_slotId, 0);
            }

            _slotId = 0;
            _keyboardEndpointId = 0;
            _keyboardTransferTrb = 0;
            KeyboardReady = false;
        }

        private static void ProcessKeyboardReport()
        {
            byte* report = _keyboardReport.Pointer;
            byte modifiers = report[0];
            bool shift = (modifiers & ((1 << 1) | (1 << 5))) != 0;

            for (int i = 2; i < 8; i++)
            {
                byte usage = report[i];
                if (usage == 0 || WasUsagePressed(usage))
                    continue;

                if (usage == 57)
                {
                    _capsLock = !_capsLock;
                    continue;
                }

                char ch = TranslateUsage(usage, shift, _capsLock);
                if (ch != '\0')
                    EnqueueKey(ch);
            }

            for (int i = 0; i < 8; i++)
                _previousReport[i] = report[i];
        }

        private static bool WasUsagePressed(byte usage)
        {
            for (int i = 2; i < 8; i++)
            {
                if (_previousReport[i] == usage)
                    return true;
            }
            return false;
        }

        private static char TranslateUsage(byte usage, bool shift, bool caps)
        {
            if (usage >= 4 && usage <= 29)
            {
                char c = (char)('a' + (usage - 4));
                bool upper = shift ^ caps;
                return upper ? (char)(c - 32) : c;
            }

            if (usage >= 30 && usage <= 39)
            {
                const string normal = "1234567890";
                const string shifted = "!@#$%^&*()";
                int index = usage - 30;
                return shift ? shifted[index] : normal[index];
            }

            switch (usage)
            {
                case 40: return '\r';
                case 41: return (char)27;
                case 42: return '\b';
                case 43: return '\t';
                case 44: return ' ';
                case 45: return shift ? '_' : '-';
                case 46: return shift ? '+' : '=';
                case 47: return shift ? '{' : '[';
                case 48: return shift ? '}' : ']';
                case 49: return shift ? '|' : '\\';
                case 51: return shift ? ':' : ';';
                case 52: return shift ? '"' : '\'';
                case 53: return shift ? '~' : '`';
                case 54: return shift ? '<' : ',';
                case 55: return shift ? '>' : '.';
                case 56: return shift ? '?' : '/';
                default: return '\0';
            }
        }

        private static void EnqueueKey(char key)
        {
            int next = (_keyQueueTail + 1) % _keyQueue.Length;
            if (next == _keyQueueHead)
                return;

            _keyQueue[_keyQueueTail] = key;
            _keyQueueTail = next;
        }

        private static bool TryDequeueKey(out char key)
        {
            if (_keyQueueHead == _keyQueueTail)
            {
                key = '\0';
                return false;
            }

            key = _keyQueue[_keyQueueHead];
            _keyQueueHead = (_keyQueueHead + 1) % _keyQueue.Length;
            return true;
        }

        private static void ClearPreviousReport()
        {
            for (int i = 0; i < _previousReport.Length; i++)
                _previousReport[i] = 0;

            _keyQueueHead = 0;
            _keyQueueTail = 0;
        }

        private static ulong SelectConfigFunction(byte bus, byte device, byte function)
        {
            uint bdf = ((uint)bus << 20) | ((uint)device << 15) | ((uint)function << 12);
            Write32(_pcieRegs + PcieExtCfgIndexOffset, bdf);
            Rpi4UsbNative.DsbIsb();
            return _pcieRegs + PcieExtCfgDataOffset;
        }

        private static bool VerifyDeviceMapping(ulong physicalAddress)
        {
            if (Limine.HHDM.Response == null)
                return false;

            ulong hhdm = Limine.HHDM.Response->Offset;
            ulong mair = Rpi4UsbNative.ReadMair();
            ulong ttbr1 = Rpi4UsbNative.ReadTtbr1() & AddressMask;
            if (ttbr1 == 0)
                return false;

            ulong* l0 = (ulong*)(ttbr1 + hhdm);
            int l0Index = (int)((physicalAddress >> 39) & 0x1FFUL);
            ulong l0e = l0[l0Index];
            if ((l0e & (DescriptorValid | DescriptorTable)) !=
                (DescriptorValid | DescriptorTable))
                return false;

            ulong* l1 = (ulong*)((l0e & AddressMask) + hhdm);
            int l1Index = (int)((physicalAddress >> 30) & 0x1FFUL);
            ulong l1e = l1[l1Index];
            if ((l1e & DescriptorValid) == 0)
                return false;

            ulong descriptor;
            if ((l1e & DescriptorTable) == 0)
            {
                descriptor = l1e;
            }
            else
            {
                ulong* l2 = (ulong*)((l1e & AddressMask) + hhdm);
                int l2Index = (int)((physicalAddress >> 21) & 0x1FFUL);
                descriptor = l2[l2Index];
                if ((descriptor & DescriptorValid) == 0)
                    return false;
            }

            int mairIndex = (int)((descriptor >> 2) & 7UL);
            byte attribute = (byte)((mair >> (mairIndex * 8)) & 0xFFUL);
            return attribute == 0x00 || attribute == 0x04;
        }

        private static uint PortNeutral(uint portSc)
        {
            return (portSc & PortReadOnlyMask) | (portSc & PortReadWriteStateMask);
        }

        private static ulong PortAddress(uint port)
        {
            return _opBase + OpPortBase + ((ulong)(port - 1) * OpPortStride);
        }

        private static bool WaitRegister(ulong address, uint mask, uint expected, uint timeoutMs)
        {
            ulong deadline = DeadlineMilliseconds(timeoutMs);
            while ((Read32(address) & mask) != expected)
            {
                if (DeadlinePassed(deadline))
                    return false;
            }
            return true;
        }

        private static ulong DeadlineMilliseconds(uint milliseconds)
        {
            ulong freq = Rpi4UsbNative.TimerFrequency();
            ulong now = Rpi4UsbNative.TimerCounter();

            if (freq == 0)
                return now + ((ulong)milliseconds * 100000UL);

            return now + ((freq * milliseconds + 999UL) / 1000UL);
        }

        private static bool DeadlinePassed(ulong deadline)
        {
            return Rpi4UsbNative.TimerCounter() >= deadline;
        }

        private static void DelayMilliseconds(uint milliseconds)
        {
            ulong deadline = DeadlineMilliseconds(milliseconds);
            while (!DeadlinePassed(deadline))
            {
            }
        }

        private static void ScheduleNextAttachPoll()
        {
            _nextAttachPoll = DeadlineMilliseconds(1000);
        }

        private static uint TrbControl(uint type) => type << 10;
        private static uint TrbType(uint control) => (control >> 10) & 0x3FU;
        private static byte CompletionCode(Trb trb) => (byte)(trb.Status >> 24);

        private static void AttachLog(string message)
        {
            if (_verboseAttach)
                Console.WriteLine(message);
        }

        private static bool Fail(string message)
        {
            LastError = message;
            Console.WriteLine("  USB keyboard init: FAILED - " + message);
            return false;
        }

        private static void ZeroBlock(DmaBlock block)
        {
            if (!block.Valid)
                return;

            for (int i = 0; i < block.Size; i++)
                block.Pointer[i] = 0;
        }

        private static void CopyBytes(byte* destination, byte* source, int count)
        {
            for (int i = 0; i < count; i++)
                destination[i] = source[i];
        }

        private static ushort ReadLe16(byte* p)
        {
            return (ushort)(p[0] | (p[1] << 8));
        }

        private static uint ReadContext32(byte* context, int dword)
        {
            return ((uint*)context)[dword];
        }

        private static void WriteContext32(byte* context, int dword, uint value)
        {
            ((uint*)context)[dword] = value;
        }

        private static void WriteContext64(byte* context, int dword, ulong value)
        {
            ((uint*)context)[dword] = (uint)value;
            ((uint*)context)[dword + 1] = (uint)(value >> 32);
        }

        private static void WriteDma32(byte* address, uint value)
        {
            *(uint*)address = value;
        }

        private static void WriteDma64(byte* address, ulong value)
        {
            *(uint*)address = (uint)value;
            *(uint*)(address + 4) = (uint)(value >> 32);
        }

        private static void Write64Mmio(ulong address, ulong value)
        {
            Write32(address, (uint)value);
            Write32(address + 4, (uint)(value >> 32));
            Rpi4UsbNative.DsbIsb();
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

        private struct Ring
        {
            public DmaBlock Block;
            public int Enqueue;
            public uint Cycle;
        }

        private struct DmaBlock
        {
            public byte* Pointer;
            public ulong Physical;
            public int Size;
            public bool Valid => Pointer != null && Physical != 0 && Size > 0;
        }

        private unsafe struct DmaArena
        {
            public byte* Base;
            public ulong PhysicalBase;
            public ulong Size;
            public ulong Offset;
            public string Error;

            public bool Initialize()
            {
                Base = (byte*)Rpi4UsbNative.DmaArenaBase();
                Size = Rpi4UsbNative.DmaArenaSize();
                Offset = 0;
                Error = string.Empty;

                if (Base == null || Size < 65536)
                {
                    Error = "native DMA arena missing/too small";
                    return false;
                }

                PhysicalBase = Rpi4UsbNative.VaToPa((ulong)Base);
                if (PhysicalBase == 0 || PhysicalBase >= DmaPhysicalLimit ||
                    PhysicalBase + Size > DmaPhysicalLimit)
                {
                    Error = "DMA arena physical range is outside PFTF's <3 GiB window";
                    return false;
                }

                for (ulong offset = 0; offset < Size; offset += 4096)
                {
                    ulong pa = Rpi4UsbNative.VaToPa((ulong)Base + offset);
                    if (pa != PhysicalBase + offset)
                    {
                        Error = "DMA arena is not physically contiguous at +" + Hex(offset);
                        return false;
                    }
                }

                return true;
            }

            public DmaBlock Allocate(int bytes, int alignment)
            {
                DmaBlock result = default;

                if (bytes <= 0 || alignment <= 0 || (alignment & (alignment - 1)) != 0)
                {
                    Error = "invalid DMA allocation request";
                    return result;
                }

                ulong aligned = (Offset + (ulong)alignment - 1UL) & ~((ulong)alignment - 1UL);
                int padded = (bytes + 63) & ~63;

                if (aligned + (ulong)padded > Size)
                {
                    Error = "DMA arena exhausted";
                    return result;
                }

                byte* pointer = (byte*)((ulong)Base + aligned);
                ulong physical = PhysicalBase + aligned;

                for (int i = 0; i < padded; i++)
                    pointer[i] = 0;

                result.Pointer = pointer;
                result.Physical = physical;
                result.Size = padded;
                Offset = aligned + (ulong)padded;
                return result;
            }
        }
    }

    internal static partial class Rpi4UsbNative
    {
        [LibraryImport("*", EntryPoint = "zonderq_dma_arena_base")]
        [SuppressGCTransition]
        internal static partial nint DmaArenaBase();

        [LibraryImport("*", EntryPoint = "zonderq_dma_arena_size")]
        [SuppressGCTransition]
        internal static partial ulong DmaArenaSize();

        [LibraryImport("*", EntryPoint = "zonderq_dma_clean")]
        [SuppressGCTransition]
        internal static partial void DmaClean(nint address, ulong length);

        [LibraryImport("*", EntryPoint = "zonderq_dma_invalidate")]
        [SuppressGCTransition]
        internal static partial void DmaInvalidate(nint address, ulong length);

        [LibraryImport("*", EntryPoint = "zonderq_dma_clean_invalidate")]
        [SuppressGCTransition]
        internal static partial void DmaCleanInvalidate(nint address, ulong length);

        [LibraryImport("*", EntryPoint = "_native_arm64_va_to_pa")]
        [SuppressGCTransition]
        internal static partial ulong VaToPa(ulong virtualAddress);

        [LibraryImport("*", EntryPoint = "_native_arm64_read_ttbr1_el1")]
        [SuppressGCTransition]
        internal static partial ulong ReadTtbr1();

        [LibraryImport("*", EntryPoint = "_native_arm64_read_mair_el1")]
        [SuppressGCTransition]
        internal static partial ulong ReadMair();

        [LibraryImport("*", EntryPoint = "_native_arm64_dsb_isb")]
        [SuppressGCTransition]
        internal static partial void DsbIsb();

        [LibraryImport("*", EntryPoint = "_native_arm64_timer_get_frequency")]
        [SuppressGCTransition]
        internal static partial ulong TimerFrequency();

        [LibraryImport("*", EntryPoint = "_native_arm64_timer_get_counter")]
        [SuppressGCTransition]
        internal static partial ulong TimerCounter();
    }
}
