using System;

namespace ZonderqOS
{
    public sealed class Rpi4BootKernel : Cosmos.Kernel.System.Kernel
    {
        private bool announced;

        protected override void BeforeRun()
        {
            Console.WriteLine("========================================");
            Console.WriteLine("  ZonderqOS ARM64 - Raspberry Pi 4");
            Console.WriteLine("  STAGE-0 BOOT TEST");
            Console.WriteLine("========================================");
            Console.WriteLine();
            Console.WriteLine("Managed kernel reached BeforeRun().");
            Console.WriteLine("No scheduler, no sleep, no input/storage/network.");
        }

        protected override void Run()
        {
            if (announced)
                return;

            Console.WriteLine();
            Console.WriteLine("Managed Run() reached.");
            announced = true;
        }
    }
}
