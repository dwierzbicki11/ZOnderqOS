using System;
using System.Collections.Generic;
using ZonderqOS.Hardware.UsbTethering;
using ZonderqOS.SystemCore.Networking;

namespace UsbTetheringSmoke
{
    internal static class Program
    {
        private static void Assert(bool cond, string msg)
        {
            if (!cond)
                throw new Exception("ASSERTION FAILED: " + msg);
        }

        private static int Main()
        {
            Console.WriteLine("[USB-NET-TEST] Starting ZonderqOS USB Tethering (RNDIS + CDC-ECM) Smoke Suite...");

            TestDescriptorClassification();
            TestRndisControlMessages();
            TestRndisTxWrapAndZlpPad();
            TestRndisRxMultiPacketAggregationAndCorruptionGuards();
            TestCdcEcmMacStringDescriptor();
            TestCdcNcmProtocol();
            TestRfc2131DhcpCodec();

            Console.WriteLine("[USB-NET-TEST] ALL USB TETHERING PROTOCOL TESTS PASSED [PASS]");
            return 0;
        }

        private static byte[] BuildConfig(byte ctrlClass, byte ctrlSub, byte ctrlProto,
            bool withUnion, bool withEthFunc, byte macIdx, bool dataOnAlt1)
        {
            var buf = new List<byte>();
            // Config descriptor (9 bytes)
            buf.AddRange(new byte[] { 9, 0x02, 0, 0, 2, 1, 0, 0xC0, 50 });
            // Control interface 0 alt 0
            buf.AddRange(new byte[] { 9, 0x04, 0, 0, 1, ctrlClass, ctrlSub, ctrlProto, 0 });
            if (withUnion)
            {
                // CDC Union Functional Descriptor: master=0, slave=1
                buf.AddRange(new byte[] { 5, 0x24, 0x06, 0, 1 });
            }
            if (withEthFunc)
            {
                // CDC Ethernet Networking Functional Descriptor (13 bytes)
                buf.AddRange(new byte[] { 13, 0x24, 0x0F, macIdx, 0, 0, 0, 0, 0xEA, 0x05, 0, 0, 0 });
            }
            // Interrupt IN endpoint on control interface
            buf.AddRange(new byte[] { 7, 0x05, 0x81, 0x03, 16, 0, 10 });

            if (dataOnAlt1)
            {
                // Data interface 1 alt 0 (0 endpoints)
                buf.AddRange(new byte[] { 9, 0x04, 1, 0, 0, 0x0A, 0x00, 0x00, 0 });
                // Data interface 1 alt 1 (Bulk IN + Bulk OUT)
                buf.AddRange(new byte[] { 9, 0x04, 1, 1, 2, 0x0A, 0x00, 0x00, 0 });
            }
            else
            {
                // Data interface 1 alt 0 (Bulk IN + Bulk OUT)
                buf.AddRange(new byte[] { 9, 0x04, 1, 0, 2, 0x0A, 0x00, 0x00, 0 });
            }
            buf.AddRange(new byte[] { 7, 0x05, 0x82, 0x02, 0, 2, 0 }); // Bulk IN
            buf.AddRange(new byte[] { 7, 0x05, 0x02, 0x02, 0, 2, 0 }); // Bulk OUT

            buf[2] = (byte)(buf.Count & 0xFF);
            buf[3] = (byte)((buf.Count >> 8) & 0xFF);
            return buf.ToArray();
        }

        private static void TestDescriptorClassification()
        {
            Console.WriteLine("  -> Testing USB descriptor parser (QEMU usb-net, Android RNDIS, CDC-ECM, non-network rejection)...");

            // 1. QEMU usb-net / Linux g_ether RNDIS (0x02 / 0x02 / 0xFF)
            byte[] qemuCfg = BuildConfig(0x02, 0x02, 0xFF, withUnion: true, withEthFunc: false, macIdx: 0, dataOnAlt1: false);
            Assert(UsbNetConfigParser.TryInspect(qemuCfg, 1, out var qemuInfo), "QEMU RNDIS config not recognized");
            Assert(qemuInfo.Kind == UsbNetProtocolKind.Rndis && qemuInfo.ControlInterfaceNumber == 0 && qemuInfo.DataAlternateSetting == 0,
                "QEMU RNDIS fields mismatch");

            // 2. Android standard Wireless RNDIS (0xE0 / 0x01 / 0x03)
            byte[] android1 = BuildConfig(0xE0, 0x01, 0x03, withUnion: false, withEthFunc: false, macIdx: 0, dataOnAlt1: false);
            Assert(UsbNetConfigParser.TryInspect(android1, 1, out var a1Info) && a1Info.Kind == UsbNetProtocolKind.Rndis,
                "Android 0xE0/0x01/0x03 RNDIS not recognized");

            // 3. Android 10+ Misc RNDIS (0xEF / 0x04 / 0x01)
            byte[] android2 = BuildConfig(0xEF, 0x04, 0x01, withUnion: false, withEthFunc: false, macIdx: 0, dataOnAlt1: false);
            Assert(UsbNetConfigParser.TryInspect(android2, 1, out var a2Info) && a2Info.Kind == UsbNetProtocolKind.Rndis,
                "Android 0xEF/0x04/0x01 RNDIS not recognized");

            // 4. CDC-ECM (0x02 / 0x06 / 0x00) with altSetting=1 and MAC string index 4
            byte[] ecmCfg = BuildConfig(0x02, 0x06, 0x00, withUnion: true, withEthFunc: true, macIdx: 4, dataOnAlt1: true);
            Assert(UsbNetConfigParser.TryInspect(ecmCfg, 1, out var ecmInfo), "CDC-ECM config not recognized");
            Assert(ecmInfo.Kind == UsbNetProtocolKind.CdcEcm && ecmInfo.MacStringDescriptorIndex == 4 &&
                   ecmInfo.DataAlternateSetting == 1 && ecmInfo.MaxSegmentSize == 1514, "CDC-ECM fields mismatch");

            // 5. Plain CDC ACM Serial Modem (0x02 / 0x02 / 0x01) MUST be declined!
            byte[] modemCfg = BuildConfig(0x02, 0x02, 0x01, withUnion: true, withEthFunc: false, macIdx: 0, dataOnAlt1: false);
            Assert(!UsbNetConfigParser.TryInspect(modemCfg, 1, out _), "CDC ACM serial modem must be rejected");
        }

        private static void TestRndisControlMessages()
        {
            Console.WriteLine("  -> Testing RNDIS INIT, QUERY MAC, SET PACKET FILTER, HALT codecs...");

            Span<byte> buf = stackalloc byte[64];
            Assert(RndisProtocol.BuildInitRequest(buf, 1, 4096) == 24, "INIT length mismatch");

            byte[] initResp = new byte[52];
            BitConverter.TryWriteBytes(initResp.AsSpan(0, 4), RndisProtocol.MsgInitComplete);
            BitConverter.TryWriteBytes(initResp.AsSpan(4, 4), 52U);
            BitConverter.TryWriteBytes(initResp.AsSpan(8, 4), 1U);
            BitConverter.TryWriteBytes(initResp.AsSpan(12, 4), RndisProtocol.StatusSuccess);
            BitConverter.TryWriteBytes(initResp.AsSpan(36, 4), 8192U);
            Assert(RndisProtocol.TryParseInitComplete(initResp, 1, out uint maxTx, out uint st) && maxTx == 8192 && st == 0,
                "INIT_COMPLETE parse failed");

            byte[] queryResp = new byte[30];
            BitConverter.TryWriteBytes(queryResp.AsSpan(0, 4), RndisProtocol.MsgQueryComplete);
            BitConverter.TryWriteBytes(queryResp.AsSpan(4, 4), 30U);
            BitConverter.TryWriteBytes(queryResp.AsSpan(8, 4), 2U);
            BitConverter.TryWriteBytes(queryResp.AsSpan(12, 4), RndisProtocol.StatusSuccess);
            BitConverter.TryWriteBytes(queryResp.AsSpan(16, 4), 6U);
            BitConverter.TryWriteBytes(queryResp.AsSpan(20, 4), 16U); // 8 + 16 = 24
            byte[] expectedMac = { 0x52, 0x54, 0x00, 0x12, 0x34, 0x56 };
            expectedMac.CopyTo(queryResp, 24);

            Span<byte> parsedMac = stackalloc byte[6];
            Assert(RndisProtocol.TryParseQueryMacComplete(queryResp, 2, parsedMac), "QUERY_MAC parse failed");
            for (int i = 0; i < 6; i++)
                Assert(parsedMac[i] == expectedMac[i], "MAC byte mismatch at " + i);

            Assert(RndisProtocol.BuildSetPacketFilterRequest(buf, 3, RndisProtocol.PacketFilterDefault) == 32, "SET length mismatch");
            byte[] setResp = new byte[16];
            BitConverter.TryWriteBytes(setResp.AsSpan(0, 4), RndisProtocol.MsgSetComplete);
            BitConverter.TryWriteBytes(setResp.AsSpan(4, 4), 16U);
            BitConverter.TryWriteBytes(setResp.AsSpan(8, 4), 3U);
            BitConverter.TryWriteBytes(setResp.AsSpan(12, 4), RndisProtocol.StatusSuccess);
            Assert(RndisProtocol.TryParseSetComplete(setResp, 3), "SET_COMPLETE parse failed");

            Assert(RndisProtocol.BuildHaltRequest(buf, 4) == 12, "HALT length mismatch");
        }

        private static void TestRndisTxWrapAndZlpPad()
        {
            Console.WriteLine("  -> Testing RNDIS TX framing and short-packet ZLP padding...");
            // 20-byte Ethernet frame + 44-byte header = 64 bytes -> exact multiple of 64! Must append 1 pad byte (65 bytes)!
            byte[] frame20 = new byte[20];
            for (int i = 0; i < frame20.Length; i++) frame20[i] = (byte)(0xA0 + i);
            byte[] outBuf = new byte[256];

            int txLen = RndisProtocol.WrapPacket(frame20, outBuf, 64);
            Assert(txLen == 65, "Expected 65 bytes (64 + 1 ZLP pad), got " + txLen);
            Assert(outBuf[64] == 0x00, "ZLP pad byte must be zero");

            // 60-byte Ethernet frame + 44-byte header = 104 bytes -> not multiple of 64 -> no extra pad
            byte[] frame60 = new byte[60];
            int txLen2 = RndisProtocol.WrapPacket(frame60, outBuf, 64);
            Assert(txLen2 == 104, "Expected 104 bytes without extra pad, got " + txLen2);

            // Undersized/oversized frames rejected
            Assert(RndisProtocol.WrapPacket(new byte[10], outBuf, 64) == 0, "Undersized frame accepted");
            Assert(RndisProtocol.WrapPacket(new byte[2000], outBuf, 64) == 0, "Oversized frame accepted");
        }

        private static void TestRndisRxMultiPacketAggregationAndCorruptionGuards()
        {
            Console.WriteLine("  -> Testing RNDIS RX multi-packet aggregation & malformed packet guards...");
            byte[] f1 = new byte[42];
            byte[] f2 = new byte[60];
            for (int i = 0; i < f1.Length; i++) f1[i] = (byte)(i + 1);
            for (int i = 0; i < f2.Length; i++) f2[i] = (byte)(0xF0 - i);

            byte[] agg = new byte[256];
            int len1 = RndisProtocol.WrapPacket(f1, agg, 512);
            int len2 = RndisProtocol.WrapPacket(f2, agg.AsSpan(len1), 512);
            int total = len1 + len2;

            int cursor = 0;
            Assert(RndisProtocol.TryUnwrapNextPacket(agg.AsSpan(0, total), ref cursor, out var out1), "First packet unwrap failed");
            Assert(out1.SequenceEqual(f1), "First frame payload mismatch");
            Assert(RndisProtocol.TryUnwrapNextPacket(agg.AsSpan(0, total), ref cursor, out var out2), "Second packet unwrap failed");
            Assert(out2.SequenceEqual(f2), "Second frame payload mismatch");
            Assert(!RndisProtocol.TryUnwrapNextPacket(agg.AsSpan(0, total), ref cursor, out _), "Unexpected third packet");

            // Corrupt MsgLength > buffer length must fail closed
            byte[] bad = new byte[64];
            BitConverter.TryWriteBytes(bad.AsSpan(0, 4), RndisProtocol.MsgPacket);
            BitConverter.TryWriteBytes(bad.AsSpan(4, 4), 9999U);
            int badCursor = 0;
            Assert(!RndisProtocol.TryUnwrapNextPacket(bad, ref badCursor, out _), "Corrupt oversized RNDIS packet accepted");
        }

        private static void TestCdcEcmMacStringDescriptor()
        {
            Console.WriteLine("  -> Testing CDC-ECM UTF-16LE MAC string descriptor parser...");
            string macHex = "525400ABCDEF";
            byte[] desc = new byte[2 + macHex.Length * 2];
            desc[0] = (byte)desc.Length;
            desc[1] = 0x03;
            for (int i = 0; i < macHex.Length; i++)
            {
                desc[2 + i * 2] = (byte)macHex[i];
                desc[3 + i * 2] = 0;
            }
            Span<byte> mac = stackalloc byte[6];
            Assert(RndisProtocol.TryParseUsbMacStringDescriptor(desc, mac), "CDC-ECM MAC parse failed");
            byte[] expected = { 0x52, 0x54, 0x00, 0xAB, 0xCD, 0xEF };
            for (int i = 0; i < 6; i++)
                Assert(mac[i] == expected[i], "CDC-ECM MAC byte mismatch at " + i);
        }

        private static void TestCdcNcmProtocol()
        {
            Console.WriteLine("  -> Testing CDC-NCM (Android 12-16) descriptor, NTB16 TX/RX, chaining & cycle guard...");

            // 1. Descriptor classification (Class 0x02 / SubClass 0x0D / Protocol 0x00)
            byte[] ncmCfg = BuildConfig(0x02, 0x0D, 0x00, withUnion: true, withEthFunc: true, macIdx: 5, dataOnAlt1: true);
            Assert(UsbNetConfigParser.TryInspect(ncmCfg, 1, out var ncmInfo), "CDC-NCM config not recognized");
            Assert(ncmInfo.Kind == UsbNetProtocolKind.CdcNcm && ncmInfo.MacStringDescriptorIndex == 5 &&
                   ncmInfo.DataAlternateSetting == 1, "CDC-NCM fields mismatch");

            // 2. GET_NTB_PARAMETERS parsing
            byte[] pbuf = new byte[28];
            BitConverter.TryWriteBytes(pbuf.AsSpan(0, 2), (ushort)28);
            BitConverter.TryWriteBytes(pbuf.AsSpan(2, 2), (ushort)0x0001); // NTB16
            BitConverter.TryWriteBytes(pbuf.AsSpan(4, 4), 16384U);
            BitConverter.TryWriteBytes(pbuf.AsSpan(16, 4), 4096U);
            BitConverter.TryWriteBytes(pbuf.AsSpan(20, 2), (ushort)4); // divisor
            BitConverter.TryWriteBytes(pbuf.AsSpan(22, 2), (ushort)2); // remainder
            BitConverter.TryWriteBytes(pbuf.AsSpan(24, 2), (ushort)8); // NDP alignment
            BitConverter.TryWriteBytes(pbuf.AsSpan(26, 2), (ushort)16);
            Assert(NcmProtocol.TryParseNtbParameters(pbuf, out NcmParameters parms), "NCM params parse failed");
            Assert(parms.NtbInMaxSize == 16384 && parms.NdpOutDivisor == 4 && parms.NdpOutPayloadRemainder == 2 &&
                   parms.NdpOutAlignment == 8, "NCM parsed parameter mismatch");

            // 3. TX WrapPacket16 + RX round-trip (verifying alignment + ZLP pad when exact multiple of 64)
            byte[] frame = new byte[60];
            for (int i = 0; i < frame.Length; i++) frame[i] = (byte)(0x30 + i);
            byte[] txBuf = new byte[256];
            int wrappedLen = NcmProtocol.WrapPacket16(frame, txBuf, 7, parms, 64);
            Assert(wrappedLen > 60 && (wrappedLen % 64) != 0, "NCM WrapPacket16 length/ZLP guard failed: " + wrappedLen);

            Assert(NcmProtocol.InitRxCursor(txBuf.AsSpan(0, wrappedLen), out NcmRxCursor rx), "NCM InitRxCursor failed");
            Assert(NcmProtocol.TryUnwrapNextPacket16(txBuf.AsSpan(0, wrappedLen), ref rx, out var unwrapped), "NCM unwrap failed");
            Assert(unwrapped.SequenceEqual(frame), "NCM round-trip payload mismatch");
            Assert(!NcmProtocol.TryUnwrapNextPacket16(txBuf.AsSpan(0, wrappedLen), ref rx, out _), "Unexpected extra NCM frame");

            // 4. Multi-datagram NTB16 with 2 chained NDP16 tables
            byte[] f1 = new byte[24];
            byte[] f2 = new byte[36];
            for (int i = 0; i < f1.Length; i++) f1[i] = (byte)(0x11 + i);
            for (int i = 0; i < f2.Length; i++) f2[i] = (byte)(0x88 + i);
            byte[] multi = new byte[128];
            // NTH16 at 0 (12 bytes): blockLen=128, ndpIndex=16
            BitConverter.TryWriteBytes(multi.AsSpan(0, 4), NcmProtocol.SigNth16);
            BitConverter.TryWriteBytes(multi.AsSpan(4, 2), (ushort)12);
            BitConverter.TryWriteBytes(multi.AsSpan(6, 2), (ushort)1);
            BitConverter.TryWriteBytes(multi.AsSpan(8, 2), (ushort)128);
            BitConverter.TryWriteBytes(multi.AsSpan(10, 2), (ushort)16);
            // NDP16 #1 at 16 (16 bytes): nextNdp=32, datagram -> offset 48, len 24
            BitConverter.TryWriteBytes(multi.AsSpan(16, 4), NcmProtocol.SigNdp16NoCrc);
            BitConverter.TryWriteBytes(multi.AsSpan(20, 2), (ushort)16);
            BitConverter.TryWriteBytes(multi.AsSpan(22, 2), (ushort)32);
            BitConverter.TryWriteBytes(multi.AsSpan(24, 2), (ushort)48);
            BitConverter.TryWriteBytes(multi.AsSpan(26, 2), (ushort)f1.Length);
            // NDP16 #2 at 32 (16 bytes): nextNdp=0, datagram -> offset 76, len 36
            BitConverter.TryWriteBytes(multi.AsSpan(32, 4), NcmProtocol.SigNdp16Crc);
            BitConverter.TryWriteBytes(multi.AsSpan(36, 2), (ushort)16);
            BitConverter.TryWriteBytes(multi.AsSpan(38, 2), (ushort)0);
            BitConverter.TryWriteBytes(multi.AsSpan(40, 2), (ushort)76);
            BitConverter.TryWriteBytes(multi.AsSpan(42, 2), (ushort)f2.Length);
            f1.CopyTo(multi, 48);
            f2.CopyTo(multi, 76);

            Assert(NcmProtocol.InitRxCursor(multi, out var mrx), "Chained NCM InitRxCursor failed");
            Assert(NcmProtocol.TryUnwrapNextPacket16(multi, ref mrx, out var mf1) && mf1.SequenceEqual(f1), "Chained NCM frame 1 mismatch");
            Assert(NcmProtocol.TryUnwrapNextPacket16(multi, ref mrx, out var mf2) && mf2.SequenceEqual(f2), "Chained NCM frame 2 mismatch");
            Assert(!NcmProtocol.TryUnwrapNextPacket16(multi, ref mrx, out _), "Unexpected 3rd chained NCM frame");

            // 5. Malformed signature & cyclic NDP16 DoS guard (must terminate cleanly!)
            Assert(!NcmProtocol.InitRxCursor(new byte[64], out _), "Zero buffer accepted as NTH16");
            byte[] cyclic = new byte[64];
            BitConverter.TryWriteBytes(cyclic.AsSpan(0, 4), NcmProtocol.SigNth16);
            BitConverter.TryWriteBytes(cyclic.AsSpan(4, 2), (ushort)12);
            BitConverter.TryWriteBytes(cyclic.AsSpan(8, 2), (ushort)64);
            BitConverter.TryWriteBytes(cyclic.AsSpan(10, 2), (ushort)16);
            BitConverter.TryWriteBytes(cyclic.AsSpan(16, 4), NcmProtocol.SigNdp16NoCrc);
            BitConverter.TryWriteBytes(cyclic.AsSpan(20, 2), (ushort)16);
            BitConverter.TryWriteBytes(cyclic.AsSpan(22, 2), (ushort)16); // self-loop!
            Assert(NcmProtocol.InitRxCursor(cyclic, out var crx), "Cyclic cursor init");
            int guardIters = 0;
            while (NcmProtocol.TryUnwrapNextPacket16(cyclic, ref crx, out _))
                guardIters++;
            Assert(guardIters == 0, "Cyclic empty NDP16 must terminate with 0 frames");
        }

        private static void TestRfc2131DhcpCodec()
        {
            Console.WriteLine("  -> Testing RFC 2131 DHCPv4 codec (Broadcast 0x8000, Option 54, checksums, arbitrary TLV order)...");
            byte[] mac = { 0x02, 0x5A, 0x51, 0xAA, 0xBB, 0xCC };
            uint xid = 0x5A511234U;
            byte[] frame = new byte[ZonderqDhcpProtocol.FullEthernetFrameSize];

            // 1. DHCPDISCOVER frame: 342 bytes, Broadcast flag 0x8000, valid IPv4 + UDP checksums
            int len = ZonderqDhcpProtocol.BuildDiscoverEthernetFrame(mac, xid, 2, frame);
            Assert(len == 342, "Expected 342-byte DHCPDISCOVER frame, got " + len);
            Assert(frame[12] == 0x08 && frame[13] == 0x00, "EtherType must be IPv4");
            // IPv4 header checksum verifies to 0
            Assert(ZonderqDhcpProtocol.ComputeChecksum(frame.AsSpan(14, 20)) == 0, "Invalid IPv4 header checksum");
            // BOOTP flags at 14 + 20 + 8 + 10 = 52 must be 0x8000 (Broadcast)
            Assert(frame[52] == 0x80 && frame[53] == 0x00, "BOOTP broadcast flag 0x8000 missing in DISCOVER");
            // Magic cookie at 42 + 236 = 278
            Assert(frame[278] == 0x63 && frame[279] == 0x82 && frame[280] == 0x53 && frame[281] == 0x63, "Magic cookie missing");

            // 2. DHCPREQUEST frame must include Option 50 (Requested IP) + Option 54 (Server Identifier) + Broadcast 0x8000
            byte[] offeredIp = { 192, 168, 42, 155 };
            byte[] serverId = { 192, 168, 42, 129 };
            int reqLen = ZonderqDhcpProtocol.BuildRequestEthernetFrame(mac, xid, 4, offeredIp, serverId, frame);
            Assert(reqLen == 342, "Expected 342-byte DHCPREQUEST frame");
            Assert(frame[52] == 0x80 && frame[53] == 0x00, "BOOTP broadcast flag 0x8000 missing in REQUEST");
            Assert(ZonderqDhcpProtocol.ComputeChecksum(frame.AsSpan(14, 20)) == 0, "Invalid REQUEST IPv4 checksum");
            bool foundOpt50 = false, foundOpt54 = false;
            int p = 42 + 240;
            while (p < frame.Length && frame[p] != 255)
            {
                byte code = frame[p++];
                if (code == 0) continue;
                byte olen = frame[p++];
                if (code == 50 && olen == 4 && frame[p] == 192 && frame[p + 3] == 155) foundOpt50 = true;
                if (code == 54 && olen == 4 && frame[p] == 192 && frame[p + 3] == 129) foundOpt54 = true;
                p += olen;
            }
            Assert(foundOpt50 && foundOpt54, "DHCPREQUEST must contain both Option 50 and Option 54");

            // 3. Parse a UNICAST Android DHCPOFFER where Option 54 & Option 1 appear BEFORE Option 53 (breaks stock Cosmos!)
            byte[] offerEth = new byte[342];
            offerEth[12] = 0x08; offerEth[13] = 0x00;
            offerEth[14] = 0x45;
            offerEth[16] = (byte)(328 >> 8); offerEth[17] = (byte)(328 & 0xFF);
            offerEth[23] = 17; // UDP
            // Src IP 192.168.42.129 -> Unicast Dst IP 192.168.42.155
            serverId.CopyTo(offerEth, 26);
            offeredIp.CopyTo(offerEth, 30);
            // UDP 67 -> 68
            offerEth[34] = 0; offerEth[35] = 67;
            offerEth[36] = 0; offerEth[37] = 68;
            offerEth[38] = (byte)(308 >> 8); offerEth[39] = (byte)(308 & 0xFF);
            // BOOTP reply at 42
            offerEth[42] = 2; offerEth[43] = 1; offerEth[44] = 6;
            offerEth[46] = 0x5A; offerEth[47] = 0x51; offerEth[48] = 0x12; offerEth[49] = 0x34;
            offeredIp.CopyTo(offerEth, 42 + 16); // yiaddr
            mac.CopyTo(offerEth, 42 + 28);       // chaddr
            offerEth[42 + 236] = 0x63; offerEth[42 + 237] = 0x82; offerEth[42 + 238] = 0x53; offerEth[42 + 239] = 0x63;
            int o = 42 + 240;
            // Put Option 0 PAD + Option 54 + Option 1 + Option 3 + Option 6 BEFORE Option 53!
            offerEth[o++] = 0;
            offerEth[o++] = 54; offerEth[o++] = 4; serverId.CopyTo(offerEth, o); o += 4;
            offerEth[o++] = 1; offerEth[o++] = 4; offerEth[o++] = 255; offerEth[o++] = 255; offerEth[o++] = 255; offerEth[o++] = 0;
            offerEth[o++] = 3; offerEth[o++] = 4; serverId.CopyTo(offerEth, o); o += 4;
            offerEth[o++] = 6; offerEth[o++] = 4; serverId.CopyTo(offerEth, o); o += 4;
            offerEth[o++] = 53; offerEth[o++] = 1; offerEth[o++] = ZonderqDhcpProtocol.MsgOffer;
            offerEth[o] = 255;

            Assert(ZonderqDhcpProtocol.TryParseReplyEthernetFrame(offerEth, mac, xid, out var parsed), "Failed to parse out-of-order unicast Android DHCPOFFER");
            Assert(parsed.MessageType == ZonderqDhcpProtocol.MsgOffer, "Offer MsgType mismatch");
            Assert(ZonderqDhcpProtocol.FormatIpv4(parsed.OfferedIp) == "192.168.42.155", "Offered IP mismatch");
            Assert(ZonderqDhcpProtocol.FormatIpv4(parsed.ServerId) == "192.168.42.129", "ServerId mismatch");
            Assert(ZonderqDhcpProtocol.FormatIpv4(parsed.Gateway) == "192.168.42.129", "Gateway mismatch");
            Assert(ZonderqDhcpProtocol.FormatIpv4(parsed.DnsServer) == "192.168.42.129", "DNS mismatch");

            // 4. Reject mismatched XID or MAC
            Assert(!ZonderqDhcpProtocol.TryParseReplyEthernetFrame(offerEth, mac, xid + 1, out _), "Accepted wrong XID");
            byte[] otherMac = { 0x02, 0x5A, 0x51, 0x00, 0x00, 0x01 };
            Assert(!ZonderqDhcpProtocol.TryParseReplyEthernetFrame(offerEth, otherMac, xid, out _), "Accepted wrong MAC");
        }
    }
}
