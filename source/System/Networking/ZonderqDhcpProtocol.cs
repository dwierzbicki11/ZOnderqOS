using System;

namespace ZonderqOS.SystemCore.Networking
{
    public sealed class DhcpReplyInfo
    {
        public byte MessageType { get; set; }
        public uint TransactionId { get; set; }
        public byte[] OfferedIp { get; set; } = new byte[4];
        public byte[] ServerId { get; set; } = new byte[4];
        public byte[] SubnetMask { get; set; } = new byte[] { 255, 255, 255, 0 };
        public byte[] Gateway { get; set; } = new byte[4];
        public byte[] DnsServer { get; set; } = new byte[4];
        public uint LeaseSeconds { get; set; }
        public bool HasServerId { get; set; }
        public bool HasGateway { get; set; }
        public bool HasDnsServer { get; set; }
    }

    /// <summary>
    /// Pure RFC 2131 / RFC 1542 DHCPv4 wire codec (zero Cosmos HAL dependencies, host-testable).
    /// Key compatibility fixes over minimal BOOTP stacks for real Android USB Tethering
    /// (DhcpServer.java / dnsmasq) and physical routers:
    /// - Sets BOOTP Broadcast Flag (0x8000) so servers reply to 255.255.255.255:68
    ///   instead of unicast yiaddr (which Cosmos IPv4Handler drops before IPConfig is enabled).
    /// - Includes Option 54 (Server Identifier) in DHCPREQUEST (mandatory on Android 10-16).
    /// - Includes Option 61 (Client Identifier = 01 + MAC) and Option 55 (Parameter Request List).
    /// - Pads BOOTP payload to standard 300-byte minimum with real IPv4 + UDP checksums.
    /// - Parses all DHCP TLV options in arbitrary order (handles Option 0 PAD & Option 255 END).
    /// </summary>
    public static class ZonderqDhcpProtocol
    {
        public const int EthernetHeaderSize = 14;
        public const int Ipv4HeaderSize = 20;
        public const int UdpHeaderSize = 8;
        public const int BootpPayloadSize = 300;
        public const int FullEthernetFrameSize = EthernetHeaderSize + Ipv4HeaderSize + UdpHeaderSize + BootpPayloadSize;

        public const ushort DhcpClientPort = 68;
        public const ushort DhcpServerPort = 67;
        public const ushort BootpFlagBroadcast = 0x8000;

        public const byte MsgDiscover = 1;
        public const byte MsgOffer = 2;
        public const byte MsgRequest = 3;
        public const byte MsgAck = 5;
        public const byte MsgNak = 6;

        public const byte OptPad = 0;
        public const byte OptSubnetMask = 1;
        public const byte OptRouter = 3;
        public const byte OptDns = 6;
        public const byte OptDomainName = 15;
        public const byte OptRequestedIp = 50;
        public const byte OptLeaseTime = 51;
        public const byte OptMessageType = 53;
        public const byte OptServerId = 54;
        public const byte OptParamRequestList = 55;
        public const byte OptClientId = 61;
        public const byte OptEnd = 255;

        public static int BuildDiscoverEthernetFrame(ReadOnlySpan<byte> clientMac, uint xid,
            ushort elapsedSecs, Span<byte> outFrame)
        {
            if (clientMac.Length < 6 || outFrame.Length < FullEthernetFrameSize)
                return 0;

            Span<byte> bootp = outFrame.Slice(EthernetHeaderSize + Ipv4HeaderSize + UdpHeaderSize, BootpPayloadSize);
            WriteBootpHeader(bootp, clientMac, xid, elapsedSecs);

            int opt = 240;
            // Option 53: DHCP Message Type = DISCOVER (1)
            bootp[opt++] = OptMessageType;
            bootp[opt++] = 1;
            bootp[opt++] = MsgDiscover;

            // Option 61: Client Identifier = Ethernet (1) + 6-byte MAC
            bootp[opt++] = OptClientId;
            bootp[opt++] = 7;
            bootp[opt++] = 1;
            clientMac.Slice(0, 6).CopyTo(bootp.Slice(opt, 6));
            opt += 6;

            // Option 55: Parameter Request List (Subnet, Router, DNS, Domain, Lease)
            bootp[opt++] = OptParamRequestList;
            bootp[opt++] = 5;
            bootp[opt++] = OptSubnetMask;
            bootp[opt++] = OptRouter;
            bootp[opt++] = OptDns;
            bootp[opt++] = OptDomainName;
            bootp[opt++] = OptLeaseTime;

            // Option 255: END
            bootp[opt] = OptEnd;

            WrapBroadcastUdpIpv4Ethernet(outFrame, clientMac, (ushort)(xid & 0xFFFF));
            return FullEthernetFrameSize;
        }

        public static int BuildRequestEthernetFrame(ReadOnlySpan<byte> clientMac, uint xid,
            ushort elapsedSecs, ReadOnlySpan<byte> requestedIp, ReadOnlySpan<byte> serverId, Span<byte> outFrame)
        {
            if (clientMac.Length < 6 || requestedIp.Length < 4 || outFrame.Length < FullEthernetFrameSize)
                return 0;

            Span<byte> bootp = outFrame.Slice(EthernetHeaderSize + Ipv4HeaderSize + UdpHeaderSize, BootpPayloadSize);
            WriteBootpHeader(bootp, clientMac, xid, elapsedSecs);

            int opt = 240;
            // Option 53: DHCP Message Type = REQUEST (3)
            bootp[opt++] = OptMessageType;
            bootp[opt++] = 1;
            bootp[opt++] = MsgRequest;

            // Option 61: Client Identifier = Ethernet (1) + 6-byte MAC
            bootp[opt++] = OptClientId;
            bootp[opt++] = 7;
            bootp[opt++] = 1;
            clientMac.Slice(0, 6).CopyTo(bootp.Slice(opt, 6));
            opt += 6;

            // Option 50: Requested IP Address
            bootp[opt++] = OptRequestedIp;
            bootp[opt++] = 4;
            requestedIp.Slice(0, 4).CopyTo(bootp.Slice(opt, 4));
            opt += 4;

            // Option 54: Server Identifier (mandatory in SELECTING state per RFC 2131 & Android DhcpServer)
            if (serverId.Length >= 4 && !IsZeroIpv4(serverId))
            {
                bootp[opt++] = OptServerId;
                bootp[opt++] = 4;
                serverId.Slice(0, 4).CopyTo(bootp.Slice(opt, 4));
                opt += 4;
            }

            // Option 55: Parameter Request List
            bootp[opt++] = OptParamRequestList;
            bootp[opt++] = 5;
            bootp[opt++] = OptSubnetMask;
            bootp[opt++] = OptRouter;
            bootp[opt++] = OptDns;
            bootp[opt++] = OptDomainName;
            bootp[opt++] = OptLeaseTime;

            // Option 255: END
            bootp[opt] = OptEnd;

            WrapBroadcastUdpIpv4Ethernet(outFrame, clientMac, (ushort)((xid + 1) & 0xFFFF));
            return FullEthernetFrameSize;
        }

        public static bool TryParseReplyEthernetFrame(ReadOnlySpan<byte> ethFrame,
            ReadOnlySpan<byte> expectedMac, uint expectedXid, out DhcpReplyInfo reply)
        {
            reply = null;
            if (ethFrame.Length < EthernetHeaderSize + Ipv4HeaderSize + UdpHeaderSize + 244)
                return false;

            // EtherType == 0x0800 (IPv4)
            if (ethFrame[12] != 0x08 || ethFrame[13] != 0x00)
                return false;

            int ipStart = EthernetHeaderSize;
            byte verIhl = ethFrame[ipStart];
            if ((verIhl >> 4) != 4)
                return false;
            int ipHeaderLen = (verIhl & 0x0F) * 4;
            if (ipHeaderLen < 20 || ipStart + ipHeaderLen + UdpHeaderSize + 244 > ethFrame.Length)
                return false;

            // Protocol == 17 (UDP)
            if (ethFrame[ipStart + 9] != 17)
                return false;

            int ipTotalLen = (ethFrame[ipStart + 2] << 8) | ethFrame[ipStart + 3];
            int frameCap = Math.Min(ethFrame.Length, ipStart + ipTotalLen);

            int udpStart = ipStart + ipHeaderLen;
            if (udpStart + UdpHeaderSize > frameCap)
                return false;

            ushort srcPort = ReadBeU16(ethFrame, udpStart);
            ushort dstPort = ReadBeU16(ethFrame, udpStart + 2);
            ushort udpLen = ReadBeU16(ethFrame, udpStart + 4);
            if (dstPort != DhcpClientPort || srcPort != DhcpServerPort)
                return false;

            int payloadStart = udpStart + UdpHeaderSize;
            int payloadLen = udpLen >= UdpHeaderSize ? Math.Min(udpLen - UdpHeaderSize, frameCap - payloadStart) : (frameCap - payloadStart);
            if (payloadLen < 244)
                return false;

            ReadOnlySpan<byte> ipSrc = ethFrame.Slice(ipStart + 12, 4);
            return TryParseReplyPayload(ethFrame.Slice(payloadStart, payloadLen), expectedMac, expectedXid, ipSrc, out reply);
        }

        public static bool TryParseReplyPayload(ReadOnlySpan<byte> payload,
            ReadOnlySpan<byte> expectedMac, uint expectedXid, ReadOnlySpan<byte> ipSourceFallback, out DhcpReplyInfo reply)
        {
            reply = null;
            if (payload.Length < 244 || expectedMac.Length < 6)
                return false;

            // op == 2 (BOOTREPLY), htype == 1 (Ethernet), hlen == 6
            if (payload[0] != 2 || payload[1] != 1 || payload[2] != 6)
                return false;

            uint xid = ReadBeU32(payload, 4);
            if (xid != expectedXid)
                return false;

            // Verify chaddr matches our MAC address
            for (int i = 0; i < 6; i++)
            {
                if (payload[28 + i] != expectedMac[i])
                    return false;
            }

            // Magic cookie: 99.130.83.99 (0x63 0x82 0x53 0x63)
            if (payload[236] != 0x63 || payload[237] != 0x82 || payload[238] != 0x53 || payload[239] != 0x63)
                return false;

            var parsed = new DhcpReplyInfo
            {
                TransactionId = xid
            };
            payload.Slice(16, 4).CopyTo(parsed.OfferedIp);

            // Walk TLV options safely in any order.
            int pos = 240;
            while (pos < payload.Length)
            {
                byte code = payload[pos++];
                if (code == OptPad)
                    continue;
                if (code == OptEnd)
                    break;
                if (pos >= payload.Length)
                    break;

                byte len = payload[pos++];
                if (pos + len > payload.Length)
                    break;

                ReadOnlySpan<byte> optData = payload.Slice(pos, len);
                pos += len;

                switch (code)
                {
                    case OptMessageType:
                        if (len >= 1)
                            parsed.MessageType = optData[0];
                        break;
                    case OptSubnetMask:
                        if (len >= 4)
                            optData.Slice(0, 4).CopyTo(parsed.SubnetMask);
                        break;
                    case OptRouter:
                        if (len >= 4)
                        {
                            optData.Slice(0, 4).CopyTo(parsed.Gateway);
                            parsed.HasGateway = !IsZeroIpv4(parsed.Gateway);
                        }
                        break;
                    case OptDns:
                        if (len >= 4)
                        {
                            optData.Slice(0, 4).CopyTo(parsed.DnsServer);
                            parsed.HasDnsServer = !IsZeroIpv4(parsed.DnsServer);
                        }
                        break;
                    case OptServerId:
                        if (len >= 4)
                        {
                            optData.Slice(0, 4).CopyTo(parsed.ServerId);
                            parsed.HasServerId = !IsZeroIpv4(parsed.ServerId);
                        }
                        break;
                    case OptLeaseTime:
                        if (len >= 4)
                            parsed.LeaseSeconds = ReadBeU32(optData, 0);
                        break;
                }
            }

            if (parsed.MessageType == 0)
                return false;

            if (!parsed.HasServerId)
            {
                // Fallback to siaddr (offset 20) or IPv4 source IP if server omitted Option 54.
                ReadOnlySpan<byte> siaddr = payload.Slice(20, 4);
                if (!IsZeroIpv4(siaddr))
                {
                    siaddr.CopyTo(parsed.ServerId);
                    parsed.HasServerId = true;
                }
                else if (ipSourceFallback.Length >= 4 && !IsZeroIpv4(ipSourceFallback))
                {
                    ipSourceFallback.Slice(0, 4).CopyTo(parsed.ServerId);
                    parsed.HasServerId = true;
                }
            }

            reply = parsed;
            return true;
        }

        private static void WriteBootpHeader(Span<byte> bootp, ReadOnlySpan<byte> clientMac, uint xid, ushort elapsedSecs)
        {
            bootp.Clear();
            bootp[0] = 1; // BOOTREQUEST
            bootp[1] = 1; // Ethernet
            bootp[2] = 6; // MAC length
            bootp[3] = 0; // hops
            WriteBeU32(bootp, 4, xid);
            WriteBeU16(bootp, 8, elapsedSecs);
            WriteBeU16(bootp, 10, BootpFlagBroadcast); // 0x8000 -> force broadcast reply
            clientMac.Slice(0, 6).CopyTo(bootp.Slice(28, 6));
            // Magic cookie
            bootp[236] = 0x63;
            bootp[237] = 0x82;
            bootp[238] = 0x53;
            bootp[239] = 0x63;
        }

        private static void WrapBroadcastUdpIpv4Ethernet(Span<byte> frame, ReadOnlySpan<byte> srcMac, ushort ipId)
        {
            // Ethernet header (0..13)
            for (int i = 0; i < 6; i++) frame[i] = 0xFF; // Broadcast dst MAC
            srcMac.Slice(0, 6).CopyTo(frame.Slice(6, 6));
            frame[12] = 0x08;
            frame[13] = 0x00;

            // IPv4 header (14..33)
            int ip = EthernetHeaderSize;
            int udpLen = UdpHeaderSize + BootpPayloadSize;
            int ipTotalLen = Ipv4HeaderSize + udpLen;

            frame[ip + 0] = 0x45; // IPv4, IHL=5 (20 bytes)
            frame[ip + 1] = 0x00; // DSCP/ECN
            WriteBeU16(frame, ip + 2, (ushort)ipTotalLen);
            WriteBeU16(frame, ip + 4, ipId);
            WriteBeU16(frame, ip + 6, 0x0000);
            frame[ip + 8] = 64;   // TTL
            frame[ip + 9] = 17;   // UDP
            frame[ip + 10] = 0;
            frame[ip + 11] = 0;
            // Src IP: 0.0.0.0
            frame[ip + 12] = 0; frame[ip + 13] = 0; frame[ip + 14] = 0; frame[ip + 15] = 0;
            // Dst IP: 255.255.255.255
            frame[ip + 16] = 255; frame[ip + 17] = 255; frame[ip + 18] = 255; frame[ip + 19] = 255;

            ushort ipCsum = ComputeChecksum(frame.Slice(ip, Ipv4HeaderSize));
            WriteBeU16(frame, ip + 10, ipCsum);

            // UDP header (34..41)
            int udp = ip + Ipv4HeaderSize;
            WriteBeU16(frame, udp + 0, DhcpClientPort);
            WriteBeU16(frame, udp + 2, DhcpServerPort);
            WriteBeU16(frame, udp + 4, (ushort)udpLen);
            WriteBeU16(frame, udp + 6, 0);

            ushort udpCsum = ComputeUdpIpv4Checksum(
                frame.Slice(ip + 12, 4),
                frame.Slice(ip + 16, 4),
                frame.Slice(udp, udpLen));
            WriteBeU16(frame, udp + 6, udpCsum);
        }

        public static ushort ComputeChecksum(ReadOnlySpan<byte> data)
        {
            uint sum = 0;
            int i = 0;
            while (i + 1 < data.Length)
            {
                sum += (uint)((data[i] << 8) | data[i + 1]);
                i += 2;
            }
            if (i < data.Length)
                sum += (uint)(data[i] << 8);

            while ((sum >> 16) != 0)
                sum = (sum & 0xFFFF) + (sum >> 16);

            return (ushort)(~sum & 0xFFFF);
        }

        public static ushort ComputeUdpIpv4Checksum(ReadOnlySpan<byte> srcIp, ReadOnlySpan<byte> dstIp, ReadOnlySpan<byte> udpSegment)
        {
            uint sum = 0;
            sum += (uint)((srcIp[0] << 8) | srcIp[1]);
            sum += (uint)((srcIp[2] << 8) | srcIp[3]);
            sum += (uint)((dstIp[0] << 8) | dstIp[1]);
            sum += (uint)((dstIp[2] << 8) | dstIp[3]);
            sum += 17U; // UDP protocol
            sum += (uint)udpSegment.Length;

            int i = 0;
            while (i + 1 < udpSegment.Length)
            {
                sum += (uint)((udpSegment[i] << 8) | udpSegment[i + 1]);
                i += 2;
            }
            if (i < udpSegment.Length)
                sum += (uint)(udpSegment[i] << 8);

            while ((sum >> 16) != 0)
                sum = (sum & 0xFFFF) + (sum >> 16);

            ushort result = (ushort)(~sum & 0xFFFF);
            return result == 0 ? (ushort)0xFFFF : result;
        }

        public static bool IsZeroIpv4(ReadOnlySpan<byte> ip)
        {
            return ip.Length < 4 || (ip[0] == 0 && ip[1] == 0 && ip[2] == 0 && ip[3] == 0);
        }

        public static string FormatIpv4(ReadOnlySpan<byte> ip)
        {
            if (ip.Length < 4)
                return "0.0.0.0";
            return ip[0] + "." + ip[1] + "." + ip[2] + "." + ip[3];
        }

        private static ushort ReadBeU16(ReadOnlySpan<byte> s, int off) => (ushort)((s[off] << 8) | s[off + 1]);
        private static uint ReadBeU32(ReadOnlySpan<byte> s, int off) =>
            ((uint)s[off] << 24) | ((uint)s[off + 1] << 16) | ((uint)s[off + 2] << 8) | s[off + 3];
        private static void WriteBeU16(Span<byte> s, int off, ushort v) { s[off] = (byte)(v >> 8); s[off + 1] = (byte)(v & 0xFF); }
        private static void WriteBeU32(Span<byte> s, int off, uint v)
        {
            s[off] = (byte)(v >> 24);
            s[off + 1] = (byte)((v >> 16) & 0xFF);
            s[off + 2] = (byte)((v >> 8) & 0xFF);
            s[off + 3] = (byte)(v & 0xFF);
        }
    }
}
