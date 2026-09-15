using System;
using System.Threading;

namespace ZonderqOS
{
    public sealed class Rpi4BootKernel : Cosmos.Kernel.System.Kernel
    {
        private bool announced;

        protected override void BeforeRun()
        {
            Console.Clear();
            Console.WriteLine("========================================");
            Console.WriteLine("  ZonderqOS ARM64 - Raspberry Pi 4");
            Console.WriteLine("  BOOT SMOKE TEST");
            Console.WriteLine("========================================");
            Console.WriteLine();
            Console.WriteLine("Managed kernel reached BeforeRun().");
            Console.WriteLine("UEFI -> Limine -> Cosmos ARM64: OK");
            Console.WriteLine();
            Console.WriteLine("Storage/network/keyboard/mouse are disabled");
            Console.WriteLine("for the current Raspberry Pi 4 smoke target.");
        }

        protected override void Run()
        {
            if (!announced)
            {
                Console.WriteLine();
                Console.WriteLine("Kernel Run() loop is alive.");
                announced = true;
            }

            Thread.Sleep(1000);
        }
    }
}
