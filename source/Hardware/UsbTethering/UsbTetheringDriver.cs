#if !ZONDERQ_NETWORK_DISABLED
#pragma warning disable COSMOS0003
using System;
using System.Threading;
using Cosmos.Kernel.HAL.Devices.Network;
using Cosmos.Kernel.HAL.DriverKit;
using Cosmos.Kernel.HAL.DriverKit.Buses.Usb;
using Cosmos.Kernel.HAL.DriverKit.Threading;

namespace ZonderqOS.Hardware.UsbTethering
{
    /// <summary>
    /// Cosmos 3.0.97 DriverKit USB Tethering & USB Ethernet driver (RNDIS 1.0 + CDC-ECM).
    /// Binds to USB CDC Data interfaces (Class 0x0A) paired with Android USB Tethering
    /// (RNDIS 0xE0/0x01/0x03, 0xEF/0x04/0x01, 0x02/0x02/0xFF) or CDC-ECM (0x02/0x06),
    /// publishes a native INetworkInterface to Cosmos NetworkManager, and runs a
    /// zero-allocation RX loop with clean hot-unplug teardown.
    /// </summary>
    [Driver(Feature = DriverFeature.Network)]
    public sealed class UsbTetheringDriver : Driver
    {
        private const byte UsbClassCdcData = 0x0A;
        private const byte UsbReqSetInterface = 0x0B;
        private const byte CdcReqSetEthernetPacketFilter = 0x43;
        private const ushort CdcPacketFilterDefault = 0x000E; // Directed | Broadcast | AllMulticast
        private const byte UsbDescTypeString = 0x03;

        private static readonly DeviceMatch[] DriverMatches =
        {
            new UsbMatch(interfaceClass: UsbClassCdcData)
        };

        private static int nextAdapterOrdinal;

        public override string Name => "UsbTetheringDriver";
        public override ReadOnlySpan<DeviceMatch> Matches => DriverMatches;

        public override ProbeResult Probe(DeviceBinding binding)
        {
            if (binding == null || binding.Node == null)
                return ProbeResult.Failed("Missing device binding.");

            if (!binding.Node.TryGetAccess<UsbAccess>(out UsbAccess usb) || usb == null)
                return ProbeResult.Declined("Node does not expose UsbAccess.");

            if (!UsbNetConfigParser.TryInspect(usb.Configuration, usb.InterfaceNumber, out UsbNetDescriptorInfo info) ||
                info == null || info.Kind == UsbNetProtocolKind.None)
            {
                return ProbeResult.Declined("CDC Data interface is not paired with RNDIS or CDC-ECM.");
            }

            UsbEndpoint bulkInEp = usb.FindEndpoint(UsbEndpointType.Bulk, isIn: true);
            UsbEndpoint bulkOutEp = usb.FindEndpoint(UsbEndpointType.Bulk, isIn: false);
            if (bulkInEp == null || bulkOutEp == null)
            {
                return ProbeResult.Declined("USB network data interface has no active bulk IN/OUT endpoints.");
            }

            if ((info.Kind == UsbNetProtocolKind.CdcEcm || info.Kind == UsbNetProtocolKind.CdcNcm) &&
                info.DataAlternateSetting != 0)
            {
                usb.ControlOut(
                    UsbRequestType.HostToDevice | UsbRequestType.Standard | UsbRequestType.Interface,
                    UsbReqSetInterface,
                    info.DataAlternateSetting,
                    info.DataInterfaceNumber);
            }

            if (!usb.OpenBulkPipe(binding, bulkInEp, out UsbPipe bulkInPipe) || bulkInPipe == null)
                return ProbeResult.Failed("Failed to open USB bulk IN pipe.");

            if (!usb.OpenBulkPipe(binding, bulkOutEp, out UsbPipe bulkOutPipe) || bulkOutPipe == null)
            {
                usb.ClosePipe(binding, bulkInPipe);
                return ProbeResult.Failed("Failed to open USB bulk OUT pipe.");
            }

            byte[] macBytes = new byte[6];
            uint maxTransfer = 4096;
            uint requestId = 1;
            NcmParameters ncmParams = NcmParameters.Default;

            if (info.Kind == UsbNetProtocolKind.Rndis)
            {
                if (!InitializeRndis(binding, usb, info.ControlInterfaceNumber, ref requestId,
                        macBytes, out maxTransfer, out string rndisErr))
                {
                    usb.ClosePipe(binding, bulkOutPipe);
                    usb.ClosePipe(binding, bulkInPipe);
                    return ProbeResult.Failed("RNDIS initialization failed: " + rndisErr);
                }
            }
            else if (info.Kind == UsbNetProtocolKind.CdcNcm)
            {
                InitializeCdcNcm(binding, usb, info, macBytes, out ncmParams, out maxTransfer);
            }
            else
            {
                InitializeCdcEcm(binding, usb, info, macBytes);
            }

            if (RndisProtocol.IsAllZeroMac(macBytes))
            {
                // Locally administered deterministic fallback MAC (02:5A:51:xx:xx:xx)
                macBytes[0] = 0x02;
                macBytes[1] = 0x5A;
                macBytes[2] = 0x51;
                macBytes[3] = (byte)(usb.InterfaceNumber ^ 0xA5);
                macBytes[4] = (byte)(bulkInEp.Address ^ bulkOutEp.Address);
                macBytes[5] = (byte)(nextAdapterOrdinal + 1);
            }

            int ordinal = Interlocked.Increment(ref nextAdapterOrdinal) - 1;
            string adapterName = "usbnet" + ordinal;
            var mac = new MacAddress(macBytes);
            DeviceLock txLock = binding.CreateLock();

            int rxBufSize = (int)Math.Max(2048U, Math.Min(maxTransfer, 8192U));
            var state = new UsbTetheringState(
                binding, usb, info, ncmParams, bulkInPipe, bulkOutPipe,
                bulkOutEp.MaxPacketSize, adapterName, mac, txLock, rxBufSize);

            binding.DriverState = state;
            NetworkSink sink = binding.PublishNetwork(state);
            state.AttachSink(sink);

            if (!binding.TryStartThread(adapterName + "-rx", state.ReceiveLoop, out DriverThread rxThread))
            {
                binding.Log("Failed to start RX thread for " + adapterName);
                state.MarkStopped();
                usb.ClosePipe(binding, bulkOutPipe);
                usb.ClosePipe(binding, bulkInPipe);
                return ProbeResult.Failed("Failed to start USB network RX thread.");
            }

            state.AttachRxThread(rxThread);
            sink.LinkChanged(true);
            binding.Log("Bound " + adapterName + " (" + info.Kind + ", ctrlIf=" + info.ControlInterfaceNumber +
                        ", dataIf=" + info.DataInterfaceNumber + ", mac=" + mac + ")");
            Network.NotifyHotpluggedAdapter(adapterName);
            return ProbeResult.Bound;
        }

        public override void OnDetach(DeviceBinding binding, DetachReason reason)
        {
            if (binding == null)
                return;

            if (binding.DriverState is UsbTetheringState state)
            {
                state.Teardown(reason.HardwarePresent);
            }
        }

        private static bool InitializeRndis(DeviceBinding binding, UsbAccess usb, byte ctrlIf,
            ref uint requestId, byte[] macOut, out uint maxTransferSize, out string error)
        {
            maxTransferSize = 4096;
            error = string.Empty;

            Span<byte> cmd = stackalloc byte[64];
            Span<byte> resp = stackalloc byte[256];

            // 1. RNDIS_MSG_INIT
            uint initReqId = requestId++;
            int initLen = RndisProtocol.BuildInitRequest(cmd, initReqId, 4096);
            if (!TryRndisControlExchange(binding, usb, ctrlIf, cmd.Slice(0, initLen), resp,
                    RndisProtocol.MsgInitComplete, out error))
            {
                return false;
            }

            if (!RndisProtocol.TryParseInitComplete(resp, initReqId, out maxTransferSize, out uint initStatus))
            {
                error = "RNDIS INIT rejected (status=0x" + initStatus.ToString("X8") + ").";
                return false;
            }

            // 2. Query current (or permanent) 802.3 MAC address
            uint macReqId = requestId++;
            int qLen = RndisProtocol.BuildQueryRequest(cmd, macReqId, RndisProtocol.Oid8023CurrentAddress);
            bool gotMac = TryRndisControlExchange(binding, usb, ctrlIf, cmd.Slice(0, qLen), resp,
                              RndisProtocol.MsgQueryComplete, out _) &&
                          RndisProtocol.TryParseQueryMacComplete(resp, macReqId, macOut);

            if (!gotMac)
            {
                macReqId = requestId++;
                qLen = RndisProtocol.BuildQueryRequest(cmd, macReqId, RndisProtocol.Oid8023PermanentAddress);
                if (TryRndisControlExchange(binding, usb, ctrlIf, cmd.Slice(0, qLen), resp,
                        RndisProtocol.MsgQueryComplete, out _))
                {
                    RndisProtocol.TryParseQueryMacComplete(resp, macReqId, macOut);
                }
            }

            // 3. Enable packet reception via OID_GEN_CURRENT_PACKET_FILTER
            uint filterReqId = requestId++;
            int fLen = RndisProtocol.BuildSetPacketFilterRequest(cmd, filterReqId, RndisProtocol.PacketFilterDefault);
            if (!TryRndisControlExchange(binding, usb, ctrlIf, cmd.Slice(0, fLen), resp,
                    RndisProtocol.MsgSetComplete, out error))
            {
                return false;
            }

            if (!RndisProtocol.TryParseSetComplete(resp, filterReqId))
            {
                error = "RNDIS SET_PACKET_FILTER returned non-success status.";
                return false;
            }

            return true;
        }

        private static bool TryRndisControlExchange(DeviceBinding binding, UsbAccess usb, byte ctrlIf,
            ReadOnlySpan<byte> command, Span<byte> response, uint expectedMsgType, out string error)
        {
            error = string.Empty;

            UsbTransferStatus sendStatus = usb.ControlOut(
                UsbRequestType.HostToDevice | UsbRequestType.Class | UsbRequestType.Interface,
                RndisProtocol.UsbReqSendEncapsulatedCommand,
                0,
                ctrlIf,
                command);

            if (sendStatus != UsbTransferStatus.Success)
            {
                error = "SEND_ENCAPSULATED_COMMAND failed (" + sendStatus + ").";
                return false;
            }

            // Phones and QEMU usb-net may need a few milliseconds before the encapsulated response is ready.
            for (int attempt = 0; attempt < 25; attempt++)
            {
                if (binding.IsDetaching || usb.IsDisconnected)
                {
                    error = "Device disconnected during RNDIS handshake.";
                    return false;
                }

                response.Clear();
                UsbTransferStatus recvStatus = usb.ControlIn(
                    UsbRequestType.DeviceToHost | UsbRequestType.Class | UsbRequestType.Interface,
                    RndisProtocol.UsbReqGetEncapsulatedResponse,
                    0,
                    ctrlIf,
                    response);

                if (recvStatus == UsbTransferStatus.Success && response.Length >= 8)
                {
                    uint msgType = (uint)(response[0] | (response[1] << 8) | (response[2] << 16) | (response[3] << 24));
                    if (msgType == expectedMsgType)
                        return true;
                }

                binding.Sleep(15);
            }

            error = "Timed out waiting for RNDIS response 0x" + expectedMsgType.ToString("X8") + ".";
            return false;
        }

        private static void InitializeCdcEcm(DeviceBinding binding, UsbAccess usb, UsbNetDescriptorInfo info, byte[] macOut)
        {
            if (info.MacStringDescriptorIndex != 0)
            {
                Span<byte> strBuf = stackalloc byte[64];
                strBuf.Clear();
                UsbTransferStatus st = usb.ControlIn(
                    UsbRequestType.DeviceToHost | UsbRequestType.Standard | UsbRequestType.Device,
                    (byte)UsbStandardRequest.GetDescriptor,
                    (ushort)((UsbDescTypeString << 8) | info.MacStringDescriptorIndex),
                    0x0409,
                    strBuf);

                if (st == UsbTransferStatus.Success)
                {
                    RndisProtocol.TryParseUsbMacStringDescriptor(strBuf, macOut);
                }
            }

            // Enable directed + broadcast + multicast reception on CDC-ECM interface.
            usb.ControlOut(
                UsbRequestType.HostToDevice | UsbRequestType.Class | UsbRequestType.Interface,
                CdcReqSetEthernetPacketFilter,
                CdcPacketFilterDefault,
                info.ControlInterfaceNumber);
        }

        private static void InitializeCdcNcm(DeviceBinding binding, UsbAccess usb, UsbNetDescriptorInfo info,
            byte[] macOut, out NcmParameters ncmParams, out uint maxRxSize)
        {
            InitializeCdcEcm(binding, usb, info, macOut);
            ncmParams = NcmParameters.Default;
            maxRxSize = 4096;

            Span<byte> paramBuf = stackalloc byte[28];
            paramBuf.Clear();
            UsbTransferStatus st = usb.ControlIn(
                UsbRequestType.DeviceToHost | UsbRequestType.Class | UsbRequestType.Interface,
                NcmProtocol.UsbReqGetNtbParameters,
                0,
                info.ControlInterfaceNumber,
                paramBuf);

            if (st == UsbTransferStatus.Success && NcmProtocol.TryParseNtbParameters(paramBuf, out NcmParameters parsed))
            {
                ncmParams = parsed;
                uint negotiatedRx = Math.Max(2048U, Math.Min(parsed.NtbInMaxSize, 8192U));
                maxRxSize = negotiatedRx;

                // Cap phone NTB IN transfer size to fit our preallocated RX buffer.
                Span<byte> inSize = stackalloc byte[4];
                inSize[0] = (byte)(negotiatedRx & 0xFF);
                inSize[1] = (byte)((negotiatedRx >> 8) & 0xFF);
                inSize[2] = (byte)((negotiatedRx >> 16) & 0xFF);
                inSize[3] = (byte)((negotiatedRx >> 24) & 0xFF);
                usb.ControlOut(
                    UsbRequestType.HostToDevice | UsbRequestType.Class | UsbRequestType.Interface,
                    NcmProtocol.UsbReqSetNtbInputSize,
                    0,
                    info.ControlInterfaceNumber,
                    inSize);
            }
        }

        private sealed class UsbTetheringState : INetworkInterface
        {
            private readonly DeviceBinding binding;
            private readonly UsbAccess usb;
            private readonly UsbNetDescriptorInfo info;
            private readonly NcmParameters ncmParams;
            private readonly UsbPipe bulkIn;
            private readonly UsbPipe bulkOut;
            private readonly int bulkOutMaxPacket;
            private readonly DeviceLock txLock;
            private readonly byte[] txBuffer = new byte[2048];
            private readonly byte[] rxBuffer;
            private NetworkSink sink;
            private DriverThread rxThread;
            private ushort ncmTxSeq;
            private bool txBusy;
            private volatile bool stopped;
            private volatile bool linkUp = true;

            public string Name { get; }
            public MacAddress MacAddress { get; }
            public bool LinkUp => linkUp && !stopped && !usb.IsDisconnected;

            public UsbTetheringState(DeviceBinding binding, UsbAccess usb, UsbNetDescriptorInfo info,
                NcmParameters ncmParams, UsbPipe bulkIn, UsbPipe bulkOut, int bulkOutMaxPacket,
                string name, MacAddress mac, DeviceLock txLock, int rxBufSize)
            {
                this.binding = binding;
                this.usb = usb;
                this.info = info;
                this.ncmParams = ncmParams;
                this.bulkIn = bulkIn;
                this.bulkOut = bulkOut;
                this.bulkOutMaxPacket = bulkOutMaxPacket > 0 ? bulkOutMaxPacket : 64;
                this.Name = name;
                this.MacAddress = mac;
                this.txLock = txLock;
                this.rxBuffer = new byte[rxBufSize];
            }

            public void AttachSink(NetworkSink networkSink) => sink = networkSink;
            public void AttachRxThread(DriverThread thread) => rxThread = thread;
            public void MarkStopped() { stopped = true; linkUp = false; }

            private bool TryClaimTx()
            {
                while (!stopped && !binding.IsDetaching && !usb.IsDisconnected)
                {
                    bool acquired = false;
                    using (txLock.Acquire())
                    {
                        if (!txBusy)
                        {
                            txBusy = true;
                            acquired = true;
                        }
                    }

                    if (acquired)
                        return true;

                    binding.Sleep(1);
                }

                return false;
            }

            private void ReleaseTx()
            {
                using (txLock.Acquire())
                {
                    txBusy = false;
                }
            }

            public bool Transmit(ReadOnlySpan<byte> frame)
            {
                if (stopped || binding.IsDetaching || usb.IsDisconnected)
                    return false;
                if (frame.Length < RndisProtocol.MinEthernetFrameBytes || frame.Length > RndisProtocol.MaxEthernetFrameBytes)
                    return false;

                // Never hold DeviceLock (IRQ-masking spinlock) across blocking USB transfers.
                if (!TryClaimTx())
                    return false;

                try
                {
                    if (stopped || binding.IsDetaching || usb.IsDisconnected)
                        return false;

                    int transferLen;
                    if (info.Kind == UsbNetProtocolKind.Rndis)
                    {
                        transferLen = RndisProtocol.WrapPacket(frame, txBuffer, bulkOutMaxPacket);
                        if (transferLen <= 0)
                            return false;
                    }
                    else if (info.Kind == UsbNetProtocolKind.CdcNcm)
                    {
                        transferLen = NcmProtocol.WrapPacket16(frame, txBuffer, ncmTxSeq++, ncmParams, bulkOutMaxPacket);
                        if (transferLen <= 0)
                            return false;
                    }
                    else
                    {
                        if (frame.Length > txBuffer.Length)
                            return false;
                        frame.CopyTo(txBuffer);
                        transferLen = frame.Length;
                    }

                    ReadOnlySpan<byte> payload = txBuffer.AsSpan(0, transferLen);
                    UsbTransferStatus status = usb.BulkOut(bulkOut, payload, out int transferred);
                    if (status == UsbTransferStatus.Stall)
                    {
                        usb.ClearHalt(bulkOut);
                        status = usb.BulkOut(bulkOut, payload, out transferred);
                    }

                    if (status == UsbTransferStatus.Disconnected)
                    {
                        linkUp = false;
                        sink?.LinkChanged(false);
                        return false;
                    }

                    if (info.Kind == UsbNetProtocolKind.CdcEcm &&
                        status == UsbTransferStatus.Success &&
                        transferred == transferLen &&
                        bulkOutMaxPacket > 0 &&
                        (transferLen % bulkOutMaxPacket) == 0 &&
                        transferLen < info.MaxSegmentSize)
                    {
                        usb.BulkOut(bulkOut, ReadOnlySpan<byte>.Empty, out _);
                    }

                    return status == UsbTransferStatus.Success && transferred == transferLen;
                }
                finally
                {
                    ReleaseTx();
                }
            }

            public void ReceiveLoop()
            {
                int consecutiveErrors = 0;

                while (!stopped && !binding.IsDetaching && !usb.IsDisconnected)
                {
                    int transferred = 0;
                    UsbTransferStatus status = usb.BulkIn(bulkIn, rxBuffer, out transferred);

                    if (status == UsbTransferStatus.Success)
                    {
                        consecutiveErrors = 0;
                        if (transferred > 0)
                        {
                            DispatchReceivedBytes(rxBuffer.AsSpan(0, Math.Min(transferred, rxBuffer.Length)));
                        }
                        continue;
                    }

                    if (status == UsbTransferStatus.Timeout)
                    {
                        // Normal idle poll timeout on USB bulk IN — yield briefly without spinning CPU.
                        consecutiveErrors = 0;
                        binding.Sleep(2);
                        continue;
                    }

                    if (status == UsbTransferStatus.Stall)
                    {
                        usb.ClearHalt(bulkIn);
                        binding.Sleep(5);
                        continue;
                    }

                    if (status == UsbTransferStatus.Disconnected)
                    {
                        break;
                    }

                    consecutiveErrors++;
                    binding.Sleep(consecutiveErrors < 5 ? 5U : 25U);
                }

                linkUp = false;
                sink?.LinkChanged(false);
            }

            private void DispatchReceivedBytes(ReadOnlySpan<byte> data)
            {
                NetworkSink currentSink = sink;
                if (currentSink == null || data.Length == 0)
                    return;

                if (info.Kind == UsbNetProtocolKind.Rndis)
                {
                    int cursor = 0;
                    while (RndisProtocol.TryUnwrapNextPacket(data, ref cursor, out ReadOnlySpan<byte> frame))
                    {
                        currentSink.Receive(frame);
                    }
                }
                else if (info.Kind == UsbNetProtocolKind.CdcNcm)
                {
                    if (NcmProtocol.InitRxCursor(data, out NcmRxCursor cursor))
                    {
                        while (NcmProtocol.TryUnwrapNextPacket16(data, ref cursor, out ReadOnlySpan<byte> frame))
                        {
                            currentSink.Receive(frame);
                        }
                    }
                }
                else
                {
                    if (data.Length >= RndisProtocol.MinEthernetFrameBytes &&
                        data.Length <= RndisProtocol.MaxEthernetFrameBytes)
                    {
                        currentSink.Receive(data);
                    }
                }
            }

            public void Teardown(bool hardwarePresent)
            {
                stopped = true;
                linkUp = false;
                sink?.LinkChanged(false);
                Network.NotifyAdapterDetached(Name);

                try
                {
                    if (rxThread != null)
                    {
                        for (int i = 0; i < 20 && !rxThread.HasExited; i++)
                            binding.Sleep(10);
                    }
                }
                catch { }

                if (hardwarePresent && info.Kind == UsbNetProtocolKind.Rndis && !usb.IsDisconnected)
                {
                    try
                    {
                        Span<byte> halt = stackalloc byte[12];
                        int len = RndisProtocol.BuildHaltRequest(halt, 0xFFFFU);
                        usb.ControlOut(
                            UsbRequestType.HostToDevice | UsbRequestType.Class | UsbRequestType.Interface,
                            RndisProtocol.UsbReqSendEncapsulatedCommand,
                            0,
                            info.ControlInterfaceNumber,
                            halt.Slice(0, len));
                    }
                    catch { }
                }

                try { usb.ClosePipe(binding, bulkOut); } catch { }
                try { usb.ClosePipe(binding, bulkIn); } catch { }
            }
        }
    }
}
#endif
