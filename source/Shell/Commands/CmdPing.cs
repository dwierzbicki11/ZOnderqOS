using System;
using Cosmos.Kernel.System.Network.IPv4;
using CosmosEndPoint = Cosmos.Kernel.System.Network.IPv4.EndPoint;

namespace ZonderqOS.Commands
{
    public class CmdPing : ICommand
    {
        private const int DefaultCount = 4;
        private const int DefaultTimeoutMs = 3000;
        private const int MaxCount = 10;
        private const int MinTimeoutMs = 250;
        private const int MaxTimeoutMs = 10000;

        public string Name => "ping";
        public string Description => "ICMP echo utility: ping <host> [count] [timeout_ms]";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
            {
                CommandIO.WriteLine("Usage: ping <host> [count] [timeout_ms]");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!Network.IsReady || Network.ActiveDevice == null)
            {
                CommandIO.WriteLine("ping: network is not configured");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            int count = DefaultCount;
            if (args.Length > 2 && (!int.TryParse(args[2], out count) || count < 1 || count > MaxCount))
            {
                CommandIO.WriteLine("ping: count must be between 1 and " + MaxCount);
                CommandIO.LastCommandSuccess = false;
                return;
            }

            int timeoutMs = DefaultTimeoutMs;
            if (args.Length > 3 && (!int.TryParse(args[3], out timeoutMs) || timeoutMs < MinTimeoutMs || timeoutMs > MaxTimeoutMs))
            {
                CommandIO.WriteLine("ping: timeout must be between " + MinTimeoutMs + " and " + MaxTimeoutMs + " ms");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string host = args[1].Trim();
            Address target = ParseAddress(host);
            if (target == null)
            {
                string resolved = Network.ResolveDns(host);
                if (string.IsNullOrEmpty(resolved))
                {
                    CommandIO.WriteLine("ping: could not resolve " + host);
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                target = ParseAddress(resolved);
                if (target == null)
                {
                    CommandIO.WriteLine("ping: DNS returned an invalid IPv4 address");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }
            }

            CommandIO.WriteLine("PING " + host + " (" + target + ")");

            int received = 0;
            int totalMs = 0;
            int minMs = Int32.MaxValue;
            int maxMs = 0;

            for (int i = 0; i < count; i++)
            {
                int elapsed = SendEcho(target, (ushort)(i + 1), timeoutMs);
                if (elapsed < 0)
                {
                    CommandIO.WriteLine("Request timeout for seq=" + (i + 1));
                    continue;
                }

                received++;
                totalMs += elapsed;
                if (elapsed < minMs) minMs = elapsed;
                if (elapsed > maxMs) maxMs = elapsed;
                CommandIO.WriteLine("Reply from " + target + ": seq=" + (i + 1) + " time=" + elapsed + " ms");
            }

            int lost = count - received;
            int lossPercent = count > 0 ? (lost * 100) / count : 100;
            CommandIO.WriteLine("");
            CommandIO.WriteLine("Ping statistics for " + target + ":");
            CommandIO.WriteLine("  Packets: Sent = " + count + ", Received = " + received + ", Lost = " + lost + " (" + lossPercent + "% loss)");

            if (received > 0)
            {
                int averageMs = totalMs / received;
                CommandIO.WriteLine("  Round trip: Min = " + minMs + " ms, Max = " + maxMs + " ms, Avg = " + averageMs + " ms");
            }

            CommandIO.LastCommandSuccess = received > 0;
        }

        private static Address ParseAddress(string value)
        {
            try
            {
                return Address.Parse(value);
            }
            catch
            {
                return null;
            }
        }

        private static int SendEcho(Address target, ushort sequence, int timeoutMs)
        {
            try
            {
                using (var client = new ICMPClient())
                {
                    client.Connect(target);
                    CosmosEndPoint source = new CosmosEndPoint(target, 0);
                    client.SendEcho(1, sequence);
                    return client.Receive(ref source, timeoutMs);
                }
            }
            catch (Exception ex)
            {
                CommandIO.WriteLine("ping: " + ex.Message);
                return -1;
            }
        }
    }
}
