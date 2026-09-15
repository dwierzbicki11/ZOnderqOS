using System;

namespace ZonderqOS
{
    /// <summary>
    /// Raspberry Pi 4 shell bring-up kernel.
    ///
    /// Uses the normal Cosmos console and the real ZonderqOS Command dispatcher.
    /// Hardware input remains intentionally disabled while the Pi-specific
    /// VL805/xHCI path is brought up one step at a time.
    /// </summary>
    public sealed class Rpi4BootKernel : Cosmos.Kernel.System.Kernel
    {
        private string currentPath = "/";
        private bool promptShown;

        protected override void BeforeRun()
        {
            Console.Clear();
            Console.WriteLine("========================================");
            Console.WriteLine(" ZonderqOS ARM64 - Raspberry Pi 4");
            Console.WriteLine(" IRQ/GIC RE-ENABLE TEST + XHCI PROBE");
            Console.WriteLine("========================================");
            Console.WriteLine();
            Console.WriteLine("UART: OFF");
            Console.WriteLine("Interrupts: ON (GIC test)");
            Console.WriteLine("Generic PCI init: OFF (isolated test)");
            Console.WriteLine("Scheduler: OFF");
            Console.WriteLine("Keyboard: OFF (xHCI probe only)");
            Console.WriteLine();

            Command.Initialize();
            Console.WriteLine("ZonderqOS Command dispatcher: OK");
            Console.WriteLine();
            Console.WriteLine("Shell self-test:");
            Console.WriteLine();

            RunSelfTest("pwd");
            RunSelfTest("echo ARM64 real shell dispatcher OK");
            RunSelfTest("echo parser-one && echo parser-two");
            RunSelfTest("help");

            Console.WriteLine();
            Rpi4XhciProbe.Run();
            Console.WriteLine();
            Console.WriteLine("Shell core ready. IRQ/GIC path reached managed code.");
            Console.WriteLine();
        }

        protected override void Run()
        {
            if (promptShown)
                return;

            Console.Write("zonderq@rpi4:" + currentPath + "$ ");
            promptShown = true;
        }

        private void RunSelfTest(string command)
        {
            Console.Write("$ ");
            Console.WriteLine(command);
            Command.Run(command, ref currentPath);
            Console.WriteLine();
        }
    }
}
