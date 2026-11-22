#if !ZONDERQ_NETWORK_DISABLED
#pragma warning disable COSMOS0002
using System;
using System.Threading;
using Cosmos.Kernel.HAL.Devices.Network;
using Cosmos.Kernel.System.Network;
using Cosmos.Kernel.System.Network.Protocols.Udp;
using CosmosAddress = Cosmos.Kernel.System.Network.Address;
using CosmosAddress4 = Cosmos.Kernel.System.Network.Address4;

namespace ZonderqOS.SystemCore.Networking
{
    /// <summary>
    /// RFC 2131-compliant DHCPv4 client for ZonderqOS over stock Cosmos 3.0.97 NetworkManager.
    /// Fixes real-hardware Android USB Tethering & router timeouts by:
    /// - Sending BOOTP Broadcast flag (0x8000), Option 54 (Server Identifier), Option 61, 300B BOOTP.
    /// - Receiving via BOTH non-blocking UdpClient(68).ReceivePacket(0) AND direct Layer-2 raw frame
    ///   observation from UsbTetheringDriver (capturing even unicast DHCPOFFER/ACK before IPConfig).
    /// - Waiting with Thread.Sleep(25) (0% CPU) instead of TimerManager busy-halts.
    /// - Retrying DISCOVER (up to 3x) and REQUEST (up to 2x) with clear diagnostics.
    /// </summary>
    public static class ZonderqDhcpClient
    {
        private static readonly object SessionLock = new object();
        private static volatile bool sessionActive;
        private static uint activeXid;
        private static byte[] activeMac;
        private static DhcpReplyInfo pendingOffer;
        private static DhcpReplyInfo pendingAck;
        private static bool receivedNak;
        private static uint nextXid = 0x5A510001U;

        /// <summary>
        /// Zero-allocation fast-path raw Ethernet frame tap called by UsbTetheringDriver.
        /// Captures DHCP replies (both broadcast and unicast) directly at Layer 2 during an active handshake.
        /// </summary>
        public static void ObserveRawEthernetFrame(ReadOnlySpan<byte> ethFrame)
        {
            if (!sessionActive)
                return;

            byte[] mac = activeMac;
            uint xid = activeXid;
            if (mac == null || mac.Length < 6)
                return;

            if (!ZonderqDhcpProtocol.TryParseReplyEthernetFrame(ethFrame, mac, xid, out DhcpReplyInfo reply) || reply == null)
                return;

            RecordReply(reply);
        }

        public static bool TryAcquireLease(NetworkAdapter adapter, out DhcpReplyInfo lease, out string error)
        {
            lease = null;
            error = string.Empty;

            if (!adapter.IsValid || !adapter.Ready || !adapter.LinkUp)
            {
                error = "Adapter link is down or not ready.";
                return false;
            }

            byte[] macBytes = ExtractMacBytes(adapter.MacAddress);
            if (macBytes == null)
            {
                error = "Adapter reported invalid MAC address.";
                return false;
            }

            // Ensure Cosmos NetworkStack.HandlePacket is attached for PCI NICs (E1000E / VirtIO-Net).
            // USB Tethering adapters already deliver raw Layer-2 frames directly via ObserveRawEthernetFrame.
            NetworkManager.Primary = adapter;
            bool isUsbNet = !string.IsNullOrEmpty(adapter.Name) &&
                            adapter.Name.StartsWith("usbnet", StringComparison.OrdinalIgnoreCase);
            if (!isUsbNet && adapter.IPConfig == null)
            {
                try { IPConfig.Enable(adapter, CosmosAddress4.Zero, CosmosAddress4.Zero, CosmosAddress4.Zero); }
                catch { }
            }

            uint xid = nextXid++;
            if (xid == 0) xid = 0x5A510001U;

            byte[] txFrame = new byte[ZonderqDhcpProtocol.FullEthernetFrameSize];
            UdpClient udpListener = null;

            lock (SessionLock)
            {
                activeXid = xid;
                activeMac = macBytes;
                pendingOffer = null;
                pendingAck = null;
                receivedNak = false;
                sessionActive = true;
            }

            try
            {
                try { udpListener = new UdpClient(ZonderqDhcpProtocol.DhcpClientPort); }
                catch { udpListener = null; }

                // Phase 1: DISCOVER -> OFFER (up to 3 attempts, 2.5s window each)
                DhcpReplyInfo offer = null;
                for (int attempt = 0; attempt < 3 && offer == null; attempt++)
                {
                    if (!adapter.LinkUp)
                    {
                        error = "Adapter link dropped during DHCP discovery.";
                        return false;
                    }

                    int len = ZonderqDhcpProtocol.BuildDiscoverEthernetFrame(
                        macBytes, xid, (ushort)(attempt * 2), txFrame);
                    if (len <= 0 || !NetworkManager.Send(txFrame, len))
                    {
                        error = "Failed to transmit DHCP DISCOVER frame on '" + adapter.Name + "'.";
                        return false;
                    }

                    offer = WaitForReply(udpListener, macBytes, xid, wantOffer: true, timeoutMs: 2500);
                }

                if (offer == null || ZonderqDhcpProtocol.IsZeroIpv4(offer.OfferedIp))
                {
                    error = "DHCP timed out waiting for DHCPOFFER (3 attempts, broadcast flag set).";
                    return false;
                }

                // Phase 2: REQUEST (with Option 54 Server Identifier) -> ACK (up to 2 attempts, 2.5s each)
                DhcpReplyInfo ack = null;
                for (int attempt = 0; attempt < 2 && ack == null; attempt++)
                {
                    if (!adapter.LinkUp)
                    {
                        error = "Adapter link dropped during DHCP request.";
                        return false;
                    }

                    int len = ZonderqDhcpProtocol.BuildRequestEthernetFrame(
                        macBytes, xid, (ushort)(attempt * 2),
                        offer.OfferedIp, offer.ServerId, txFrame);
                    if (len <= 0 || !NetworkManager.Send(txFrame, len))
                    {
                        error = "Failed to transmit DHCP REQUEST frame on '" + adapter.Name + "'.";
                        return false;
                    }

                    ack = WaitForReply(udpListener, macBytes, xid, wantOffer: false, timeoutMs: 2500);
                    if (receivedNak)
                    {
                        error = "DHCP server declined request (DHCPNAK).";
                        return false;
                    }
                }

                if (ack == null || ZonderqDhcpProtocol.IsZeroIpv4(ack.OfferedIp))
                {
                    error = "DHCP timed out waiting for DHCPACK (offer was " +
                            ZonderqDhcpProtocol.FormatIpv4(offer.OfferedIp) + ").";
                    return false;
                }

                // Merge subnet/gateway/DNS from OFFER if server omitted them in ACK.
                if (!ack.HasGateway && offer.HasGateway)
                {
                    Buffer.BlockCopy(offer.Gateway, 0, ack.Gateway, 0, 4);
                    ack.HasGateway = true;
                }
                if (!ack.HasDnsServer && offer.HasDnsServer)
                {
                    Buffer.BlockCopy(offer.DnsServer, 0, ack.DnsServer, 0, 4);
                    ack.HasDnsServer = true;
                }

                var ipAddr = new CosmosAddress4(ack.OfferedIp[0], ack.OfferedIp[1], ack.OfferedIp[2], ack.OfferedIp[3]);
                var maskAddr = new CosmosAddress4(ack.SubnetMask[0], ack.SubnetMask[1], ack.SubnetMask[2], ack.SubnetMask[3]);
                CosmosAddress gwAddr = ack.HasGateway
                    ? new CosmosAddress4(ack.Gateway[0], ack.Gateway[1], ack.Gateway[2], ack.Gateway[3])
                    : CosmosAddress4.Zero;

                if (!IPConfig.Enable(adapter, ipAddr, maskAddr, gwAddr))
                {
                    error = "IPConfig.Enable rejected DHCP lease " + ipAddr + ".";
                    return false;
                }

                if (ack.HasDnsServer)
                {
                    var dnsAddr = new CosmosAddress4(ack.DnsServer[0], ack.DnsServer[1], ack.DnsServer[2], ack.DnsServer[3]);
                    try { DnsConfig.Add(dnsAddr); } catch { }
                }
                else if (ack.HasGateway)
                {
                    // Many phone USB tether hotspots act as DNS forwarder at the gateway IP.
                    try { DnsConfig.Add(gwAddr); } catch { }
                }

                lease = ack;
                return true;
            }
            finally
            {
                try { udpListener?.Close(); } catch { }
                lock (SessionLock)
                {
                    sessionActive = false;
                    activeMac = null;
                }
            }
        }

        private static void DrainUdpPackets(UdpClient udpListener, byte[] mac, uint xid)
        {
            if (udpListener == null)
                return;

            // ReceivePacket(0) is strictly non-blocking (skips TimerManager wait loop -> 0% CPU).
            for (int i = 0; i < 8; i++)
            {
                UdpPacket packet = null;
                try { packet = udpListener.ReceivePacket(0); }
                catch { break; }

                if (packet == null)
                    break;

                byte[] payload = packet.GetUdpData();
                if (payload == null || payload.Length < 244)
                    continue;

                Span<byte> srcIp = stackalloc byte[4];
                srcIp.Clear();
                try
                {
                    CosmosAddress src = packet.SourceIP;
                    if (src != null && src.IsIpv4)
                    {
                        MaskedAddress parts = src.Parts;
                        srcIp[0] = parts[0];
                        srcIp[1] = parts[1];
                        srcIp[2] = parts[2];
                        srcIp[3] = parts[3];
                    }
                }
                catch { }

                if (ZonderqDhcpProtocol.TryParseReplyPayload(payload, mac, xid, srcIp, out DhcpReplyInfo reply) && reply != null)
                {
                    RecordReply(reply);
                }
            }
        }

        private static void RecordReply(DhcpReplyInfo reply)
        {
            lock (SessionLock)
            {
                if (!sessionActive || reply.TransactionId != activeXid)
                    return;

                if (reply.MessageType == ZonderqDhcpProtocol.MsgOffer && pendingOffer == null)
                {
                    pendingOffer = reply;
                }
                else if (reply.MessageType == ZonderqDhcpProtocol.MsgAck && pendingAck == null)
                {
                    pendingAck = reply;
                }
                else if (reply.MessageType == ZonderqDhcpProtocol.MsgNak)
                {
                    receivedNak = true;
                }
            }
        }

        private static DhcpReplyInfo WaitForReply(UdpClient udpListener, byte[] mac, uint xid, bool wantOffer, int timeoutMs)
        {
            int waited = 0;
            while (waited < timeoutMs)
            {
                DrainUdpPackets(udpListener, mac, xid);

                lock (SessionLock)
                {
                    if (wantOffer && pendingOffer != null)
                        return pendingOffer;
                    if (!wantOffer && (pendingAck != null || receivedNak))
                        return pendingAck;
                }

                // True scheduler sleep (parks thread -> 0% CPU).
                Thread.Sleep(25);
                waited += 25;
            }

            return null;
        }

        private static byte[] ExtractMacBytes(MacAddress mac)
        {
            if (mac == null)
                return null;

            string text = mac.ToString();
            if (string.IsNullOrEmpty(text))
                return null;

            string[] parts = text.Split(':');
            if (parts.Length != 6)
                return null;

            byte[] result = new byte[6];
            for (int i = 0; i < 6; i++)
            {
                if (!byte.TryParse(parts[i], System.Globalization.NumberStyles.HexNumber, null, out result[i]))
                    return null;
            }
            return result;
        }
    }
}
#endif
