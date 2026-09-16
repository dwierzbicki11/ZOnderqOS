using System;

namespace ZonderqOS
{
    /// <summary>
    /// ARM64 QEMU shell kernel.
    ///
    /// The QEMU virt machine exposes the input device through VirtIO MMIO.
    /// Cosmos' ARM64 platform initializer discovers that keyboard and backs
    /// System.Console input with the normal Cosmos keyboard manager.
    /// </summary>
    public sealed class Arm64QemuKernel : Cosmos.Kernel.System.Kernel
    {
        private string currentPath = "/";

        protected override void BeforeRun()
        {
            Console.Clear();
            Console.WriteLine("========================================");
            Console.WriteLine(" ZonderqOS ARM64 - QEMU virt");
            Console.WriteLine(" VIRTIO KEYBOARD SHELL");
            Console.WriteLine("========================================");
            Console.WriteLine();
            Console.WriteLine("Interrupts/GIC: ON");
            Console.WriteLine("UART/PL011:     ON");
            Console.WriteLine("Keyboard:       VirtIO MMIO");
            Console.WriteLine("Storage:        OFF");
            Console.WriteLine("Network:        OFF");
            Console.WriteLine("Scheduler:      OFF");
            Console.WriteLine();

            Command.Initialize();
            Console.WriteLine("ZonderqOS Command dispatcher: OK");
            Console.WriteLine("Type 'help' to list the ARM64-safe commands.");
            Console.WriteLine();
        }

        protected override void Run()
        {
            Console.Write("zonderq@qemu-arm64:" + currentPath + "$ ");

            string input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
                return;

            Command.Run(input, ref currentPath);
            Console.WriteLine();
        }
    }
}
