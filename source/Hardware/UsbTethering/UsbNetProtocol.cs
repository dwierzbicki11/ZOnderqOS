using System;
using System.Text;

namespace ZonderqOS.Hardware.UsbTethering
{
    public enum UsbNetProtocolKind
    {
        None = 0,
        Rndis = 1,
        CdcEcm = 2,
        CdcNcm = 3
    }

    public sealed class UsbNetDescriptorInfo
    {
        public UsbNetProtocolKind Kind { get; set; } = UsbNetProtocolKind.None;
        public byte ControlInterfaceNumber { get; set; }
        public byte DataInterfaceNumber { get; set; }
        public byte DataAlternateSetting { get; set; }
        public byte ControlClass { get; set; }
        public byte ControlSubclass { get; set; }
        public byte ControlProtocol { get; set; }
        public byte MacStringDescriptorIndex { get; set; }
        public ushort MaxSegmentSize { get; set; } = 1514;
    }

    /// <summary>
    /// Pure USB Configuration Descriptor inspector for USB Tethering devices:
    /// - RNDIS (Android 0xE0/0x01/0x03, Android/Windows 0xEF/0x04/0x01, CDC ACM RNDIS 0x02/0x02/0xFF,
    ///   and generic CDC ACM 0x02/0x02/0x00 paired with CDC Data 0x0A/0x00/0x00)
    /// - CDC-ECM (0x02/0x06/0x00 + CDC Ethernet Networking Functional Descriptor 0x24/0x0F)
    /// </summary>
    public static class UsbNetConfigParser
    {
        private const byte DescInterface = 0x04;
        private const byte DescEndpoint = 0x05;
        private const byte DescInterfaceAssociation = 0x0B;
        private const byte DescCsInterface = 0x24;

        private const byte CdcSubUnion = 0x06;
        private const byte CdcSubEthernet = 0x0F;

        private struct InterfaceSummary
        {
            public bool Present;
            public byte Number;
            public byte AltSetting;
            public byte Class;
            public byte Subclass;
            public byte Protocol;
            public bool HasBulkIn;
            public bool HasBulkOut;
            public byte UnionSlaveInterface;
            public bool HasUnion;
            public byte MacStringIndex;
            public ushort MaxSegmentSize;
        }

        public static bool TryInspect(ReadOnlySpan<byte> configDescriptor, byte dataInterfaceNumber,
            out UsbNetDescriptorInfo info)
        {
            info = null;
            if (configDescriptor.Length < 9)
                return false;

            // Track up to 16 interfaces by interface number (alt 0 metadata + best bulk alt).
            var controlCandidates = new InterfaceSummary[16];
            byte dataBestAlt = 0;
            bool dataFound = false;
            bool dataHasBulk = false;

            // Track IAD groupings: if an IAD covers dataInterfaceNumber, its first interface is the control interface.
            bool iadMatched = false;
            byte iadControlIf = 0;

            int currentIfNum = -1;
            byte currentAlt = 0;
            byte currentClass = 0;
            byte currentSubclass = 0;
            byte currentProtocol = 0;
            bool currentAltHasBulkIn = false;
            bool currentAltHasBulkOut = false;

            int offset = 0;
            while (offset + 2 <= configDescriptor.Length)
            {
                byte len = configDescriptor[offset];
                byte type = configDescriptor[offset + 1];
                if (len < 2 || offset + len > configDescriptor.Length)
                    break;

                if (type == DescInterfaceAssociation && len >= 8)
                {
                    byte firstIf = configDescriptor[offset + 2];
                    byte ifCount = configDescriptor[offset + 3];
                    if (dataInterfaceNumber >= firstIf && dataInterfaceNumber < firstIf + ifCount &&
                        dataInterfaceNumber != firstIf)
                    {
                        iadMatched = true;
                        iadControlIf = firstIf;
                    }
                }
                else if (type == DescInterface && len >= 9)
                {
                    // Commit previous data interface alt-setting bulk summary if applicable.
                    if (currentIfNum == dataInterfaceNumber && currentAltHasBulkIn && currentAltHasBulkOut && !dataHasBulk)
                    {
                        dataBestAlt = currentAlt;
                        dataHasBulk = true;
                    }

                    currentIfNum = configDescriptor[offset + 2];
                    currentAlt = configDescriptor[offset + 3];
                    currentClass = configDescriptor[offset + 5];
                    currentSubclass = configDescriptor[offset + 6];
                    currentProtocol = configDescriptor[offset + 7];
                    currentAltHasBulkIn = false;
                    currentAltHasBulkOut = false;

                    if (currentIfNum == dataInterfaceNumber && currentClass == 0x0A)
                    {
                        dataFound = true;
                    }

                    if (currentIfNum >= 0 && currentIfNum < controlCandidates.Length && currentAlt == 0)
                    {
                        controlCandidates[currentIfNum].Present = true;
                        controlCandidates[currentIfNum].Number = (byte)currentIfNum;
                        controlCandidates[currentIfNum].AltSetting = currentAlt;
                        controlCandidates[currentIfNum].Class = currentClass;
                        controlCandidates[currentIfNum].Subclass = currentSubclass;
                        controlCandidates[currentIfNum].Protocol = currentProtocol;
                    }
                }
                else if (type == DescEndpoint && len >= 7)
                {
                    byte epAddr = configDescriptor[offset + 2];
                    byte epAttr = configDescriptor[offset + 3];
                    bool isBulk = (epAttr & 0x03) == 0x02;
                    bool isIn = (epAddr & 0x80) != 0;
                    if (isBulk)
                    {
                        if (isIn) currentAltHasBulkIn = true;
                        else currentAltHasBulkOut = true;

                        if (currentIfNum >= 0 && currentIfNum < controlCandidates.Length && currentAlt == 0)
                        {
                            if (isIn) controlCandidates[currentIfNum].HasBulkIn = true;
                            else controlCandidates[currentIfNum].HasBulkOut = true;
                        }
                    }
                }
                else if (type == DescCsInterface && len >= 3 && currentIfNum >= 0 && currentIfNum < controlCandidates.Length)
                {
                    byte subType = configDescriptor[offset + 2];
                    if (subType == CdcSubUnion && len >= 5)
                    {
                        controlCandidates[currentIfNum].HasUnion = true;
                        controlCandidates[currentIfNum].UnionSlaveInterface = configDescriptor[offset + 4];
                    }
                    else if (subType == CdcSubEthernet && len >= 13)
                    {
                        controlCandidates[currentIfNum].MacStringIndex = configDescriptor[offset + 3];
                        ushort seg = (ushort)(configDescriptor[offset + 8] | (configDescriptor[offset + 9] << 8));
                        controlCandidates[currentIfNum].MaxSegmentSize = seg >= 576 && seg <= 9000 ? seg : (ushort)1514;
                    }
                }

                offset += len;
            }

            // Finalize last parsed interface.
            if (currentIfNum == dataInterfaceNumber && currentAltHasBulkIn && currentAltHasBulkOut && !dataHasBulk)
            {
                dataBestAlt = currentAlt;
                dataHasBulk = true;
            }

            if (!dataFound)
                return false;

            // Find the matching control interface in priority order:
            // 1. CDC Union Functional Descriptor pointing directly to dataInterfaceNumber
            // 2. Interface Association Descriptor (IAD) grouping control + data
            // 3. Immediately preceding interface (dataInterfaceNumber - 1)
            int ctrlIdx = -1;
            for (int i = 0; i < controlCandidates.Length; i++)
            {
                if (controlCandidates[i].Present && controlCandidates[i].HasUnion &&
                    controlCandidates[i].UnionSlaveInterface == dataInterfaceNumber)
                {
                    ctrlIdx = i;
                    break;
                }
            }

            if (ctrlIdx < 0 && iadMatched && iadControlIf < controlCandidates.Length && controlCandidates[iadControlIf].Present)
            {
                ctrlIdx = iadControlIf;
            }

            if (ctrlIdx < 0 && dataInterfaceNumber > 0 && dataInterfaceNumber - 1 < controlCandidates.Length &&
                controlCandidates[dataInterfaceNumber - 1].Present)
            {
                ctrlIdx = dataInterfaceNumber - 1;
            }

            if (ctrlIdx < 0)
                return false;

            InterfaceSummary ctrl = controlCandidates[ctrlIdx];
            UsbNetProtocolKind kind = ClassifyControlInterface(ctrl.Class, ctrl.Subclass, ctrl.Protocol);
            if (kind == UsbNetProtocolKind.None)
                return false;

            info = new UsbNetDescriptorInfo
            {
                Kind = kind,
                ControlInterfaceNumber = ctrl.Number,
                DataInterfaceNumber = dataInterfaceNumber,
                DataAlternateSetting = dataBestAlt,
                ControlClass = ctrl.Class,
                ControlSubclass = ctrl.Subclass,
                ControlProtocol = ctrl.Protocol,
                MacStringDescriptorIndex = ctrl.MacStringIndex,
                MaxSegmentSize = ctrl.MaxSegmentSize >= 576 ? ctrl.MaxSegmentSize : (ushort)1514
            };
            return true;
        }

        public static UsbNetProtocolKind ClassifyControlInterface(byte cls, byte sub, byte proto)
        {
            // CDC-ECM (USB Ethernet Control Model)
            if (cls == 0x02 && sub == 0x06)
                return UsbNetProtocolKind.CdcEcm;

            // CDC-NCM (USB Network Control Model — modern Android 12-16 Pixel/AOSP/OneUI USB tethering)
            if (cls == 0x02 && sub == 0x0D)
                return UsbNetProtocolKind.CdcNcm;

            // RNDIS variants used by Android USB Tethering, Windows, Linux g_ether, and QEMU usb-net:
            // - 0x02 / 0x02 / 0xFF : CDC ACM Vendor-specific (QEMU usb-net Config 1, Linux g_ether)
            // - 0xE0 / 0x01 / 0x03 : Wireless Controller RNDIS (standard Android USB tethering)
            // - 0xEF / 0x04 / 0x01 : Misc ActiveSync RNDIS (Android 10+ / Windows RNDIS)
            // - 0xEF / 0x04 / 0x04 : Misc RNDIS over Ethernet
            if (cls == 0x02 && sub == 0x02 && proto == 0xFF)
                return UsbNetProtocolKind.Rndis;
            if (cls == 0xE0 && sub == 0x01 && proto == 0x03)
                return UsbNetProtocolKind.Rndis;
            if (cls == 0xEF && sub == 0x04 && (proto == 0x01 || proto == 0x04))
                return UsbNetProtocolKind.Rndis;

            return UsbNetProtocolKind.None;
        }
    }

    /// <summary>
    /// Pure Microsoft Remote NDIS (RNDIS 1.0) control and data-packet framing codec.
    /// Zero heap allocations in hot TX/RX paths.
    /// </summary>
    public static class RndisProtocol
    {
        public const uint MsgPacket = 0x00000001U;
        public const uint MsgInit = 0x00000002U;
        public const uint MsgHalt = 0x00000003U;
        public const uint MsgQuery = 0x00000004U;
        public const uint MsgSet = 0x00000005U;
        public const uint MsgKeepAlive = 0x00000008U;

        public const uint MsgInitComplete = 0x80000002U;
        public const uint MsgQueryComplete = 0x80000004U;
        public const uint MsgSetComplete = 0x80000005U;
        public const uint MsgKeepAliveComplete = 0x80000008U;

        public const uint StatusSuccess = 0x00000000U;

        public const uint OidGenCurrentPacketFilter = 0x0001010EU;
        public const uint Oid8023PermanentAddress = 0x01010101U;
        public const uint Oid8023CurrentAddress = 0x01010102U;

        // Directed | Multicast | AllMulticast | Broadcast
        public const uint PacketFilterDefault = 0x0000000FU;

        public const int PacketHeaderSize = 44;
        public const int MinEthernetFrameBytes = 14;
        public const int MaxEthernetFrameBytes = 1522;

        // CDC Encapsulated Command / Response standard bRequest codes.
        public const byte UsbReqSendEncapsulatedCommand = 0x00;
        public const byte UsbReqGetEncapsulatedResponse = 0x01;

        public static int BuildInitRequest(Span<byte> buffer, uint requestId, uint maxTransferSize)
        {
            if (buffer.Length < 24)
                return 0;
            buffer.Slice(0, 24).Clear();
            WriteU32(buffer, 0, MsgInit);
            WriteU32(buffer, 4, 24);
            WriteU32(buffer, 8, requestId);
            WriteU32(buffer, 12, 1); // MajorVersion
            WriteU32(buffer, 16, 0); // MinorVersion
            WriteU32(buffer, 20, maxTransferSize);
            return 24;
        }

        public static bool TryParseInitComplete(ReadOnlySpan<byte> response, uint expectedRequestId,
            out uint deviceMaxTransferSize, out uint status)
        {
            deviceMaxTransferSize = 0;
            status = 0xFFFFFFFFU;
            if (response.Length < 52)
                return false;

            uint msgType = ReadU32(response, 0);
            uint msgLen = ReadU32(response, 4);
            uint reqId = ReadU32(response, 8);
            status = ReadU32(response, 12);

            if (msgType != MsgInitComplete || msgLen < 52 || reqId != expectedRequestId || status != StatusSuccess)
                return false;

            deviceMaxTransferSize = ReadU32(response, 36);
            if (deviceMaxTransferSize < 1600)
                deviceMaxTransferSize = 4096;
            return true;
        }

        public static int BuildQueryRequest(Span<byte> buffer, uint requestId, uint oid)
        {
            if (buffer.Length < 28)
                return 0;
            buffer.Slice(0, 28).Clear();
            WriteU32(buffer, 0, MsgQuery);
            WriteU32(buffer, 4, 28);
            WriteU32(buffer, 8, requestId);
            WriteU32(buffer, 12, oid);
            WriteU32(buffer, 16, 0); // InfoBufferLength
            WriteU32(buffer, 20, 0); // InfoBufferOffset
            WriteU32(buffer, 24, 0); // DeviceVcHandle
            return 28;
        }

        public static bool TryParseQueryMacComplete(ReadOnlySpan<byte> response, uint expectedRequestId,
            Span<byte> macOut)
        {
            if (response.Length < 24 || macOut.Length < 6)
                return false;

            uint msgType = ReadU32(response, 0);
            uint msgLen = ReadU32(response, 4);
            uint reqId = ReadU32(response, 8);
            uint status = ReadU32(response, 12);
            uint infoLen = ReadU32(response, 16);
            uint infoOff = ReadU32(response, 20);

            if (msgType != MsgQueryComplete || reqId != expectedRequestId || status != StatusSuccess)
                return false;
            if (infoLen < 6)
                return false;

            long payloadStart = 8L + infoOff;
            if (payloadStart < 24 || payloadStart + 6 > response.Length || (msgLen > 0 && payloadStart + 6 > msgLen))
                return false;

            response.Slice((int)payloadStart, 6).CopyTo(macOut);
            return !IsAllZeroMac(macOut);
        }

        public static int BuildSetPacketFilterRequest(Span<byte> buffer, uint requestId, uint filterMask)
        {
            if (buffer.Length < 32)
                return 0;
            buffer.Slice(0, 32).Clear();
            WriteU32(buffer, 0, MsgSet);
            WriteU32(buffer, 4, 32);
            WriteU32(buffer, 8, requestId);
            WriteU32(buffer, 12, OidGenCurrentPacketFilter);
            WriteU32(buffer, 16, 4);  // InfoBufferLength
            WriteU32(buffer, 20, 20); // InfoBufferOffset (from offset 8 -> 28)
            WriteU32(buffer, 24, 0);  // DeviceVcHandle
            WriteU32(buffer, 28, filterMask);
            return 32;
        }

        public static bool TryParseSetComplete(ReadOnlySpan<byte> response, uint expectedRequestId)
        {
            if (response.Length < 16)
                return false;
            return ReadU32(response, 0) == MsgSetComplete &&
                   ReadU32(response, 8) == expectedRequestId &&
                   ReadU32(response, 12) == StatusSuccess;
        }

        public static int BuildHaltRequest(Span<byte> buffer, uint requestId)
        {
            if (buffer.Length < 12)
                return 0;
            WriteU32(buffer, 0, MsgHalt);
            WriteU32(buffer, 4, 12);
            WriteU32(buffer, 8, requestId);
            return 12;
        }

        /// <summary>
        /// Wraps an Ethernet frame inside an RNDIS_MSG_PACKET (44-byte header).
        /// Appends a single trailing 0x00 short-packet terminator byte when the transfer
        /// length is an exact multiple of maxPacketSize (required by Android/QEMU RNDIS).
        /// </summary>
        public static int WrapPacket(ReadOnlySpan<byte> ethernetFrame, Span<byte> outBuffer, int bulkOutMaxPacketSize)
        {
            if (ethernetFrame.Length < MinEthernetFrameBytes || ethernetFrame.Length > MaxEthernetFrameBytes)
                return 0;

            int msgLen = PacketHeaderSize + ethernetFrame.Length;
            bool needShortPad = bulkOutMaxPacketSize > 0 && (msgLen % bulkOutMaxPacketSize) == 0;
            int totalTransferLen = msgLen + (needShortPad ? 1 : 0);
            if (outBuffer.Length < totalTransferLen)
                return 0;

            outBuffer.Slice(0, PacketHeaderSize).Clear();
            WriteU32(outBuffer, 0, MsgPacket);
            WriteU32(outBuffer, 4, (uint)msgLen);
            WriteU32(outBuffer, 8, PacketHeaderSize - 8); // DataOffset = 36
            WriteU32(outBuffer, 12, (uint)ethernetFrame.Length);
            ethernetFrame.CopyTo(outBuffer.Slice(PacketHeaderSize));
            if (needShortPad)
                outBuffer[msgLen] = 0x00;

            return totalTransferLen;
        }

        /// <summary>
        /// Extracts the next Ethernet frame from an RNDIS bulk IN transfer buffer (supporting
        /// single or multi-packet RNDIS aggregation used by Android phones).
        /// Advances cursor past the consumed RNDIS_MSG_PACKET.
        /// </summary>
        public static bool TryUnwrapNextPacket(ReadOnlySpan<byte> rxData, ref int cursor,
            out ReadOnlySpan<byte> ethernetFrame)
        {
            ethernetFrame = ReadOnlySpan<byte>.Empty;

            while (cursor + PacketHeaderSize <= rxData.Length)
            {
                ReadOnlySpan<byte> slice = rxData.Slice(cursor);
                uint msgType = ReadU32(slice, 0);
                uint msgLen = ReadU32(slice, 4);
                uint dataOff = ReadU32(slice, 8);
                uint dataLen = ReadU32(slice, 12);

                // Trailing 1-byte zero short-packet terminator or invalid header stops scanning.
                if (msgType != MsgPacket || msgLen < PacketHeaderSize || msgLen > (uint)(rxData.Length - cursor))
                {
                    cursor = rxData.Length;
                    return false;
                }

                long frameStart = 8L + dataOff;
                long frameEnd = frameStart + dataLen;
                int advance = (int)msgLen;
                // RNDIS multi-packet messages may be 4-byte aligned.
                cursor += advance;

                if (dataOff < PacketHeaderSize - 8 ||
                    dataLen < MinEthernetFrameBytes ||
                    dataLen > MaxEthernetFrameBytes ||
                    frameEnd > msgLen)
                {
                    continue;
                }

                ethernetFrame = slice.Slice((int)frameStart, (int)dataLen);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Parses a USB UTF-16LE String Descriptor containing 12 ASCII hex characters (CDC-ECM MAC).
        /// </summary>
        public static bool TryParseUsbMacStringDescriptor(ReadOnlySpan<byte> stringDescriptor, Span<byte> macOut)
        {
            if (stringDescriptor.Length < 26 || macOut.Length < 6)
                return false;

            byte descLen = stringDescriptor[0];
            byte descType = stringDescriptor[1];
            if (descType != 0x03 || descLen < 26 || descLen > stringDescriptor.Length)
                return false;

            for (int i = 0; i < 6; i++)
            {
                int hiIdx = 2 + (i * 4);
                int loIdx = hiIdx + 2;
                int hi = HexNibble(stringDescriptor[hiIdx]);
                int lo = HexNibble(stringDescriptor[loIdx]);
                if (hi < 0 || lo < 0 || stringDescriptor[hiIdx + 1] != 0 || stringDescriptor[loIdx + 1] != 0)
                    return false;
                macOut[i] = (byte)((hi << 4) | lo);
            }

            return !IsAllZeroMac(macOut);
        }

        public static bool IsAllZeroMac(ReadOnlySpan<byte> mac)
        {
            if (mac.Length < 6)
                return true;
            for (int i = 0; i < 6; i++)
            {
                if (mac[i] != 0)
                    return false;
            }
            return true;
        }

        private static int HexNibble(byte b)
        {
            if (b >= (byte)'0' && b <= (byte)'9') return b - (byte)'0';
            if (b >= (byte)'a' && b <= (byte)'f') return 10 + (b - (byte)'a');
            if (b >= (byte)'A' && b <= (byte)'F') return 10 + (b - (byte)'A');
            return -1;
        }

        private static uint ReadU32(ReadOnlySpan<byte> s, int offset)
        {
            return (uint)(s[offset] |
                         (s[offset + 1] << 8) |
                         (s[offset + 2] << 16) |
                         (s[offset + 3] << 24));
        }

        private static void WriteU32(Span<byte> s, int offset, uint val)
        {
            s[offset] = (byte)(val & 0xFF);
            s[offset + 1] = (byte)((val >> 8) & 0xFF);
            s[offset + 2] = (byte)((val >> 16) & 0xFF);
            s[offset + 3] = (byte)((val >> 24) & 0xFF);
        }
    }

    public struct NcmParameters
    {
        public uint NtbInMaxSize;
        public uint NtbOutMaxSize;
        public ushort NdpOutDivisor;
        public ushort NdpOutPayloadRemainder;
        public ushort NdpOutAlignment;
        public ushort NtbOutMaxDatagrams;

        public static NcmParameters Default => new NcmParameters
        {
            NtbInMaxSize = 4096,
            NtbOutMaxSize = 2048,
            NdpOutDivisor = 4,
            NdpOutPayloadRemainder = 0,
            NdpOutAlignment = 4,
            NtbOutMaxDatagrams = 16
        };
    }

    public struct NcmRxCursor
    {
        public int BlockLength;
        public int CurrentNdpOffset;
        public int CurrentNdpEnd;
        public int NextEntryOffset;
        public int NextNdpOffset;
        public int HopsRemaining;
        public bool Active;
    }

    /// <summary>
    /// Pure USB CDC-NCM 1.0 (16-bit NTB: NTH16 + NDP16) framing codec used by
    /// modern Android 12-16 USB tethering and USB-C Ethernet gadgets.
    /// Zero heap allocations in hot TX/RX paths.
    /// </summary>
    public static class NcmProtocol
    {
        public const byte UsbReqGetNtbParameters = 0x80;
        public const byte UsbReqSetNtbInputSize = 0x86;

        public const uint SigNth16 = 0x484D434EU; // "NCMH"
        public const uint SigNdp16NoCrc = 0x304D434EU; // "NCM0"
        public const uint SigNdp16Crc = 0x314D434EU; // "NCM1"

        public const int Nth16Size = 12;
        public const int Ndp16MinSize = 16;

        public static bool TryParseNtbParameters(ReadOnlySpan<byte> data, out NcmParameters parameters)
        {
            parameters = NcmParameters.Default;
            if (data.Length < 28)
                return false;

            ushort len = ReadU16(data, 0);
            ushort formats = ReadU16(data, 2);
            if (len < 28 || (formats & 0x0001) == 0)
                return false;

            uint inMax = ReadU32(data, 4);
            uint outMax = ReadU32(data, 16);
            ushort outDiv = ReadU16(data, 20);
            ushort outRem = ReadU16(data, 22);
            ushort outAlign = ReadU16(data, 24);
            ushort outMaxDg = ReadU16(data, 26);

            if (inMax < 2048) inMax = 2048;
            if (outMax < 2048) outMax = 2048;
            if (outDiv == 0 || outDiv > 64) outDiv = 4;
            if (outRem >= outDiv) outRem = 0;
            if (outAlign < 4 || outAlign > 64) outAlign = 4;

            parameters = new NcmParameters
            {
                NtbInMaxSize = inMax,
                NtbOutMaxSize = outMax,
                NdpOutDivisor = outDiv,
                NdpOutPayloadRemainder = outRem,
                NdpOutAlignment = outAlign,
                NtbOutMaxDatagrams = outMaxDg == 0 ? (ushort)16 : outMaxDg
            };
            return true;
        }

        public static int WrapPacket16(ReadOnlySpan<byte> ethernetFrame, Span<byte> outBuffer,
            ushort sequence, NcmParameters parameters, int bulkOutMaxPacketSize)
        {
            if (ethernetFrame.Length < RndisProtocol.MinEthernetFrameBytes ||
                ethernetFrame.Length > RndisProtocol.MaxEthernetFrameBytes)
            {
                return 0;
            }

            int ndpAlign = parameters.NdpOutAlignment >= 4 && parameters.NdpOutAlignment <= 64
                ? parameters.NdpOutAlignment
                : 4;
            int divisor = parameters.NdpOutDivisor >= 1 && parameters.NdpOutDivisor <= 64
                ? parameters.NdpOutDivisor
                : 4;
            int remainder = parameters.NdpOutPayloadRemainder < divisor
                ? parameters.NdpOutPayloadRemainder
                : 0;

            int ndpOffset = AlignUp(Nth16Size, ndpAlign);
            int minPayloadStart = ndpOffset + Ndp16MinSize;
            int payloadOffset = AlignWithRemainder(minPayloadStart, divisor, remainder);
            int totalLen = payloadOffset + ethernetFrame.Length;

            int maxAllowed = outBuffer.Length;
            if (parameters.NtbOutMaxSize >= 2048 && parameters.NtbOutMaxSize < (uint)maxAllowed)
                maxAllowed = (int)parameters.NtbOutMaxSize;

            // Avoid ZLP ambiguity when transfer length is an exact multiple of bulk wMaxPacketSize.
            if (bulkOutMaxPacketSize > 0 && (totalLen % bulkOutMaxPacketSize) == 0 && totalLen + 1 <= maxAllowed)
            {
                totalLen++;
            }

            if (totalLen > maxAllowed || totalLen > ushort.MaxValue)
                return 0;

            outBuffer.Slice(0, payloadOffset).Clear();
            // NTH16 (12 bytes)
            WriteU32(outBuffer, 0, SigNth16);
            WriteU16(outBuffer, 4, Nth16Size);
            WriteU16(outBuffer, 6, sequence);
            WriteU16(outBuffer, 8, (ushort)totalLen);
            WriteU16(outBuffer, 10, (ushort)ndpOffset);

            // NDP16 (16 bytes: 8-byte header + 1 datagram entry + 1 null terminator entry)
            WriteU32(outBuffer, ndpOffset + 0, SigNdp16NoCrc);
            WriteU16(outBuffer, ndpOffset + 4, Ndp16MinSize);
            WriteU16(outBuffer, ndpOffset + 6, 0); // wNextNdpIndex
            WriteU16(outBuffer, ndpOffset + 8, (ushort)payloadOffset);
            WriteU16(outBuffer, ndpOffset + 10, (ushort)ethernetFrame.Length);
            WriteU16(outBuffer, ndpOffset + 12, 0);
            WriteU16(outBuffer, ndpOffset + 14, 0);

            ethernetFrame.CopyTo(outBuffer.Slice(payloadOffset));
            if (totalLen > payloadOffset + ethernetFrame.Length)
                outBuffer[totalLen - 1] = 0x00;

            return totalLen;
        }

        public static bool InitRxCursor(ReadOnlySpan<byte> rxData, out NcmRxCursor cursor)
        {
            cursor = default;
            if (rxData.Length < Nth16Size + Ndp16MinSize)
                return false;

            uint sig = ReadU32(rxData, 0);
            ushort hdrLen = ReadU16(rxData, 4);
            ushort blockLen = ReadU16(rxData, 8);
            ushort ndpIndex = ReadU16(rxData, 10);

            if (sig != SigNth16 || hdrLen < Nth16Size)
                return false;
            if (blockLen < Nth16Size + Ndp16MinSize || blockLen > rxData.Length)
                return false;
            if (ndpIndex < hdrLen || ndpIndex + Ndp16MinSize > blockLen)
                return false;

            cursor.BlockLength = blockLen;
            cursor.NextNdpOffset = ndpIndex;
            cursor.HopsRemaining = 16;
            cursor.Active = true;
            return AdvanceToNextNdp(rxData, ref cursor);
        }

        public static bool TryUnwrapNextPacket16(ReadOnlySpan<byte> rxData, ref NcmRxCursor cursor,
            out ReadOnlySpan<byte> ethernetFrame)
        {
            ethernetFrame = ReadOnlySpan<byte>.Empty;
            if (!cursor.Active || cursor.BlockLength > rxData.Length)
                return false;

            while (cursor.Active)
            {
                while (cursor.NextEntryOffset + 4 <= cursor.CurrentNdpEnd)
                {
                    ushort dgIndex = ReadU16(rxData, cursor.NextEntryOffset);
                    ushort dgLength = ReadU16(rxData, cursor.NextEntryOffset + 2);
                    cursor.NextEntryOffset += 4;

                    if (dgIndex == 0 && dgLength == 0)
                        break;

                    if (dgLength < RndisProtocol.MinEthernetFrameBytes ||
                        dgLength > RndisProtocol.MaxEthernetFrameBytes)
                    {
                        continue;
                    }

                    if (dgIndex < Nth16Size || dgIndex + dgLength > cursor.BlockLength)
                        continue;

                    ethernetFrame = rxData.Slice(dgIndex, dgLength);
                    return true;
                }

                if (!AdvanceToNextNdp(rxData, ref cursor))
                    return false;
            }

            return false;
        }

        private static bool AdvanceToNextNdp(ReadOnlySpan<byte> rxData, ref NcmRxCursor cursor)
        {
            if (cursor.NextNdpOffset == 0 || cursor.HopsRemaining <= 0)
            {
                cursor.Active = false;
                return false;
            }

            cursor.HopsRemaining--;
            int off = cursor.NextNdpOffset;
            if (off < Nth16Size || off + Ndp16MinSize > cursor.BlockLength)
            {
                cursor.Active = false;
                return false;
            }

            uint sig = ReadU32(rxData, off);
            ushort ndpLen = ReadU16(rxData, off + 4);
            ushort nextNdp = ReadU16(rxData, off + 6);
            if ((sig != SigNdp16NoCrc && sig != SigNdp16Crc) ||
                ndpLen < Ndp16MinSize || off + ndpLen > cursor.BlockLength)
            {
                cursor.Active = false;
                return false;
            }

            cursor.CurrentNdpOffset = off;
            cursor.CurrentNdpEnd = off + ndpLen;
            cursor.NextEntryOffset = off + 8;
            cursor.NextNdpOffset = nextNdp;
            return true;
        }

        private static int AlignUp(int value, int alignment)
        {
            int rem = value % alignment;
            return rem == 0 ? value : value + (alignment - rem);
        }

        private static int AlignWithRemainder(int minValue, int divisor, int remainder)
        {
            int curRem = minValue % divisor;
            int delta = (remainder - curRem + divisor) % divisor;
            return minValue + delta;
        }

        private static ushort ReadU16(ReadOnlySpan<byte> s, int offset)
        {
            return (ushort)(s[offset] | (s[offset + 1] << 8));
        }

        private static uint ReadU32(ReadOnlySpan<byte> s, int offset)
        {
            return (uint)(s[offset] |
                         (s[offset + 1] << 8) |
                         (s[offset + 2] << 16) |
                         (s[offset + 3] << 24));
        }

        private static void WriteU16(Span<byte> s, int offset, ushort val)
        {
            s[offset] = (byte)(val & 0xFF);
            s[offset + 1] = (byte)((val >> 8) & 0xFF);
        }

        private static void WriteU32(Span<byte> s, int offset, uint val)
        {
            s[offset] = (byte)(val & 0xFF);
            s[offset + 1] = (byte)((val >> 8) & 0xFF);
            s[offset + 2] = (byte)((val >> 16) & 0xFF);
            s[offset + 3] = (byte)((val >> 24) & 0xFF);
        }
    }
}
