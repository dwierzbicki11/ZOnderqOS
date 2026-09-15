using System;

namespace ZonderqOS
{
    /// <summary>
    /// Raspberry Pi 4 shell bring-up kernel.
    ///
    /// Uses the normal Cosmos console and the real ZonderqOS Command dispatcher.
    /// IRQ/GIC initialization is enabled and has reached this managed stage on the
    /// physical Pi. USB input remains intentionally disabled while VL805/xHCI is
    /// brought up in small, diagnosable steps.
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
            Console.WriteLine(" IRQ/GIC OK + BCM2711 XHCI PROBE");
            Console.WriteLine("========================================");
            Console.WriteLine();
            Console.WriteLine("UART: OFF");
            Console.WriteLine("Interrupts: ON (managed path reached)");
            Console.WriteLine("Generic PCI init: OFF (Pi-specific probe used)");
            Console.WriteLine("Scheduler: OFF");
            Console.WriteLine("Keyboard: OFF (USB/xHCI bring-up in progress)");
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
            Console.WriteLine("Shell core ready. IRQ/GIC managed path OK; xHCI probe returned.");
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
