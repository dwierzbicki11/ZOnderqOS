using System;
using System.Threading;
using Sys = Cosmos.Kernel.System;
namespace ZonderqOS { public sealed class Kernel : Sys.Kernel { protected override void BeforeRun(){Console.WriteLine("[NETWORK-N5] starting ICMP echo probe");IcmpStageProbe.Run();} protected override void Run(){Thread.Sleep(1000);} } }
