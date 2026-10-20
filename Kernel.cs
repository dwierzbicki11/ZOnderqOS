using System;
using System.Collections.Generic;
using System.Threading;
using ZonderqOS.SystemCore;
using ZonderqOS.SystemCore.Services;
using ZonderqOS.GUI;
using Sys = Cosmos.Kernel.System;

namespace ZonderqOS
{
    public class Kernel : Sys.Kernel
    {
        private const int MaxHistoryEntries = 100;
#if ZONDERQ_INPUT_DIAGNOSTIC_BOOT
        private const bool DiagnosticConsoleBoot = true;
#else
        private const bool DiagnosticConsoleBoot = false;
#endif
        private string path = "/root";
        private readonly List<string> history = new List<string>();
        private string sessionUser;
        private bool graphicalStartupFailed;

        protected override void BeforeRun()
        {
            try
            {
                BootTelemetry.Initialize();
                Console.Clear();
#if ZONDERQ_READONLY_HARDWARE_PROBE
                Console.WriteLine("ZOnderqOS / Cosmos 3.0.94 - USB/PCI input probe");
                Console.WriteLine("READ-ONLY diagnostic boot: no disk mount, no account setup.");
                Console.WriteLine();
                InputBusDiagnostics.PrintReport();
                Console.WriteLine();
                Console.WriteLine("Commands: inputscan | mouse | mousegui | volumes | help");
                Console.WriteLine("The USB mouse should be reported as a bound pointer.");
                return;
#endif
#if ZONDERQ_HTOP_SMOKE_TEST
                HtopSmokeTest.Run();
#endif
                if (!Disk.Initialize())
                    throw new InvalidOperationException(
                        "No approved ZONDERQDATA volume: refusing normal boot before any filesystem/user writes.");
                UserManager.Initialize();
                Command.Initialize();
                EnvironmentManager.Initialize();
                SecurityLogger.Initialize();
                PermissionManager.Initialize();
                SystemLogger.Initialize();
                BootSessionHealth.Initialize();
                SystemGuardian.Initialize();
                SystemSettings.Load();
                if (DiagnosticConsoleBoot)
                {
                    Console.WriteLine("ZOnderqOS - physical hardware input diagnostics");
                    Console.WriteLine("Console-only test boot; graphical auto-start is disabled in this build.");
                    InputBusDiagnostics.PrintReport();
                    Console.WriteLine();
                }
                SystemIdentity.Initialize();
                ServiceManager.Initialize();

                UserManager.PrepareLogin();
                WriteMessage.WriteOK("ZonderqOS kernel successfully booted.", "SYS");
                Console.WriteLine();
                if (DiagnosticConsoleBoot)
                {
                    Console.WriteLine("Starting diagnostic login console.");
                    Console.WriteLine("After login use: inputscan, lspci -v, dmesg");
                }
                else
                {
                    Console.WriteLine("Starting ZOnderqOS graphical session...");
                    Console.WriteLine("Console login is used only as a recovery fallback if GUI startup fails.");
                }
            }
            catch (Exception ex)
            {
                KernelPanic.Show(ex, "BOOT", false);
            }
        }

        protected override void Run()
        {
            try
            {
#if ZONDERQ_READONLY_HARDWARE_PROBE
                RunReadOnlyInputConsole();
                return;
#endif
                if (!DiagnosticConsoleBoot && SystemSettings.BootToGui && !graphicalStartupFailed)
                {
                    if (!SecurityContext.IsAuthenticated)
                    {
                        sessionUser = null;

                        if (UserManager.RequiresInitialRootPasswordSetup())
                        {
                            if (!InitialSetupManager.Run())
                            {
                                EnterConsoleRecovery("graphical initial setup failed");
                                return;
                            }

                            UserManager.PrepareLogin();
                            return;
                        }

                        if (!LoginScreenManager.Run())
                        {
                            EnterConsoleRecovery("graphical login failed");
                            return;
                        }

                        if (!SecurityContext.IsAuthenticated)
                        {
                            EnterConsoleRecovery("graphical login returned without an authenticated session");
                            return;
                        }
                    }

                    SynchronizeSession();

                    try
                    {
                        GuiManager manager = new GuiManager();
                        manager.Run();
                        if (manager.Failed)
                            EnterConsoleRecovery("desktop manager crashed");
                        return;
                    }
                    catch (Exception ex)
                    {
                        WriteMessage.WriteError("Automatic GUI session failed: " + ex.Message, "GUI");
                        EnterConsoleRecovery("desktop manager crashed");
                        return;
                    }
                }

                if (!SecurityContext.IsAuthenticated)
                {
                    sessionUser = null;

                    if (UserManager.RequiresInitialRootPasswordSetup())
                    {
                        RunInitialRootSetupPrompt();
                        return;
                    }

                    RunLoginPrompt();
                    return;
                }

                SynchronizeSession();

                string user = EnvironmentManager.Get("USER");
                if (string.IsNullOrEmpty(user))
                    user = SecurityContext.CurrentUser;

                string host = EnvironmentManager.Get("HOSTNAME");
                if (string.IsNullOrEmpty(host))
                    host = "ZonderqOS";

                string prompt = user + "@" + host + ":" + path + "$ ";
                Console.Write(prompt);
                string command = ReadLineWithHistory(prompt);

                if (!string.IsNullOrWhiteSpace(command))
                {
                    Command.Run(command, ref path);

                    // Password-bearing account commands are deliberately excluded from
                    // history even when they appear in a later ';', '&&' or pipeline segment.
                    // History is also cleared whenever the authenticated user changes.
                    if (SecurityContext.IsAuthenticated && !IsSensitiveCommand(command) &&
                        (history.Count == 0 || history[history.Count - 1] != command))
                    {
                        history.Add(command);
                        while (history.Count > MaxHistoryEntries)
                            history.RemoveAt(0);
                    }
                }
            }
            catch (Exception ex)
            {
                KernelPanic.Show(ex, "RUNTIME", true);
            }
        }

#if ZONDERQ_READONLY_HARDWARE_PROBE
        // Completely isolated read-only diagnostic shell: no filesystem,
        // authentication database, startup services or writes to host disks.
        private static void RunReadOnlyInputConsole()
        {
            Console.Write("input-diag> ");
            string command = Console.ReadLine();
            if (command == null)
                return;

            command = command.Trim().ToLowerInvariant();
            if (command == "inputscan" || command == "scan")
            {
                InputBusDiagnostics.PrintReport();
                return;
            }

            if (command == "volumes" || command == "lsblk")
            {
                // Read-only: never create a VFS mount or mutate an on-disk BPB.
                PrintReadOnlyVolumes();
                return;
            }

            if (command == "mousegui" || command == "cursor")
            {
                // Full-screen pointer demo without mounting Kali's EFI.
                try
                {
                    MouseGuiDiagnostic.Run();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("mousegui: " + ex.Message);
                }
                return;
            }

            if (command == "mouse")
            {
                // Access the static class through fully qualified member names
                // rather than instantiating a pointer or guessing a device.
                Console.WriteLine("Move/click the USB mouse for 6 seconds...");
                int lastX = Cosmos.Kernel.System.Input.MouseManager.X;
                int lastY = Cosmos.Kernel.System.Input.MouseManager.Y;
                bool lastLeft = Cosmos.Kernel.System.Input.MouseManager.LeftButton;
                bool lastRight = Cosmos.Kernel.System.Input.MouseManager.RightButton;
                int events = 0;
                for (int i = 0; i < 60; i++)
                {
                    Thread.Sleep(100);
                    int x = Cosmos.Kernel.System.Input.MouseManager.X;
                    int y = Cosmos.Kernel.System.Input.MouseManager.Y;
                    bool left = Cosmos.Kernel.System.Input.MouseManager.LeftButton;
                    bool right = Cosmos.Kernel.System.Input.MouseManager.RightButton;
                    if (x != lastX || y != lastY || left != lastLeft || right != lastRight)
                    {
                        events++;
                        lastX = x;
                        lastY = y;
                        lastLeft = left;
                        lastRight = right;
                    }
                }
                Console.WriteLine("Observed mouse state changes: " + events);
                Console.WriteLine("Position: " + lastX + "," + lastY +
                    "  buttons L=" + lastLeft + " R=" + lastRight);
                if (events == 0)
                    Console.WriteLine("No motion seen: verify driver binding via inputscan.");
                return;
            }

            if (command == "help" || command == "?")
            {
                Console.WriteLine("inputscan - read-only PCI, USB node and pointer-driver report");
                Console.WriteLine("mouse     - sample actual pointer movement and clicks for 6 seconds");
                Console.WriteLine("mousegui  - read-only fullscreen cursor and button test (ESC to exit)");
                Console.WriteLine("volumes   - inspect disks and validate dedicated FAT32 data policy (read-only)");
                Console.WriteLine("help      - print this help");
                Console.WriteLine("No storage writes or privileged shell commands are enabled.");
                return;
            }

            if (command.Length != 0)
                Console.WriteLine("Unknown/blocked diagnostic command. Type help.");
        }
        private static void PrintReadOnlyVolumes()
        {
            try
            {
                var devices = Cosmos.Kernel.System.Storage.StorageManager.Devices;
                var partitions = Cosmos.Kernel.System.Storage.StorageManager.Partitions;
                Console.WriteLine("READ-ONLY disk inspection: NO mounts, writes or formatting.");
                Console.WriteLine("Devices: " + devices.Count + ", partitions: " + partitions.Count);
                int count = partitions.Count < 32 ? partitions.Count : 32;
                for (int i = 0; i < count; i++)
                {
                    var p = partitions[i];
                    ulong sizeMiB = p.BlockSize == 0 ? 0 :
                        (p.BlockCount / 1024UL) * p.BlockSize / 1024UL;
                    bool approved = DataVolumePolicy.IsApprovedDataPartition(p);
                    Console.WriteLine("  part" + i + " " + p.Name +
                        " " + sizeMiB + " MiB  writable-root-candidate=" +
                        (approved ? "YES" : "NO"));
                }
                if (partitions.Count > count)
                    Console.WriteLine("... additional partitions omitted");

                if (DataVolumePolicy.TryFindDataPartition(out var data, out string error))
                    Console.WriteLine("Selected data volume: " + data.Name + " (not mounted).");
                else
                    Console.WriteLine("No writable root selected: " + error);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Read-only volume inspection failed: " + ex.Message);
            }
        }
#endif

        private void EnterConsoleRecovery(string reason)
        {
            graphicalStartupFailed = true;

            if (!SecurityContext.IsAuthenticated)
                UserManager.PrepareLogin();

            Console.Clear();
            WriteMessage.WriteError("GUI startup unavailable: " + reason + ".", "GUI");
            Console.WriteLine("Entering recovery console. Reboot the system to retry the graphical boot path.");
            Console.WriteLine();
        }

        private void RunInitialRootSetupPrompt()
        {
            Console.Clear();
            if (DiagnosticConsoleBoot)
            {
                InputBusDiagnostics.PrintReport();
                Console.WriteLine();
            }
            Console.WriteLine("ZOnderqOS INITIAL SECURITY SETUP");
            Console.WriteLine("The factory root/root credential must be replaced before login.");
            Console.WriteLine(PasswordPolicy.Summary);
            Console.WriteLine();

            Console.Write("new root password: ");
            string first = ReadPassword();

            string reason;
            if (!PasswordPolicy.Validate(first, out reason))
            {
                first = null;
                Console.WriteLine(reason);
                Thread.Sleep(900);
                return;
            }

            Console.Write("confirm password: ");
            string second = ReadPassword();
            if (first != second)
            {
                first = null;
                second = null;
                Console.WriteLine("Passwords do not match.");
                Thread.Sleep(900);
                return;
            }

            bool ok = UserManager.CompleteInitialRootPasswordSetup(first);
            first = null;
            second = null;

            if (!ok)
            {
                Console.WriteLine("Could not save the new root credential. Setup will retry.");
                Thread.Sleep(1000);
                return;
            }

            UserManager.PrepareLogin();
            Console.WriteLine("Root password changed. Secure login is ready.");
            Thread.Sleep(600);
        }

        private void RunLoginPrompt()
        {
            // Never allow the recovery console to bypass first-boot hardening.
            if (UserManager.RequiresInitialRootPasswordSetup())
            {
                RunInitialRootSetupPrompt();
                return;
            }

            string issue = SystemIdentity.ReadIssue();
            if (!string.IsNullOrEmpty(issue))
                Console.Write(issue);

            Console.Write("login: ");
            string username = Console.ReadLine();
            if (username != null)
                username = username.Trim();

            int retryAfter;
            if (!AuthenticationGuard.CanAttempt(username, out retryAfter))
            {
                Console.WriteLine("Authentication temporarily blocked. Retry in " + retryAfter + " s.");
                Thread.Sleep(System.Math.Min(2000, retryAfter * 250));
                return;
            }

            Console.Write("password: ");
            string password = ReadPassword();

            if (UserManager.TryStartSession(username, password))
            {
                AuthenticationGuard.RecordSuccess(username);
                password = null;
                history.Clear();
                sessionUser = SecurityContext.CurrentUser;
                path = SecurityContext.CurrentHome;
                if (string.IsNullOrEmpty(path))
                    path = "/";

                Console.WriteLine("Welcome, " + SecurityContext.CurrentUser + ".");
                string motd = SystemIdentity.ReadMotd();
                if (!string.IsNullOrEmpty(motd))
                    Console.Write(motd);
                Console.WriteLine(DiagnosticConsoleBoot
                    ? "Diagnostic console active. GUI auto-start disabled for this build."
                    : "Recovery console active. Type 'gui' to retry the graphical desktop manually.");
                Console.WriteLine();
                return;
            }

            password = null;
            AuthenticationGuard.RecordFailure(username);
            retryAfter = AuthenticationGuard.GetRetryAfterSeconds(username);
            Console.WriteLine(retryAfter > 0
                ? "Authentication failed. Temporary delay: " + retryAfter + " s."
                : "Authentication failed.");

            Thread.Sleep(retryAfter > 0 ? System.Math.Min(2500, retryAfter * 300) : 600);
        }

        private void SynchronizeSession()
        {
            string currentUser = SecurityContext.CurrentUser ?? string.Empty;
            if (sessionUser == currentUser)
                return;

            history.Clear();
            sessionUser = currentUser;
            path = SecurityContext.CurrentHome;
            if (string.IsNullOrEmpty(path))
                path = "/";
        }

        private static bool IsSensitiveCommand(string command)
        {
            return SensitiveCommandPolicy.ContainsPasswordBearingCommand(command);
        }

        private string ReadPassword()
        {
            string password = string.Empty;
            while (true)
            {
                ConsoleKeyInfo keyInfo = Console.ReadKey(true);
                if (keyInfo.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return password;
                }

                if (keyInfo.Key == ConsoleKey.Backspace)
                {
                    if (password.Length > 0)
                    {
                        password = password.Substring(0, password.Length - 1);
                        Console.Write("\b \b");
                    }
                    continue;
                }

                if (keyInfo.KeyChar >= 32 && keyInfo.KeyChar <= 126 && password.Length < PasswordPolicy.MaxLength)
                {
                    password += keyInfo.KeyChar;
                    Console.Write('*');
                }
            }
        }

        private string ReadLineWithHistory(string prompt)
        {
            string currentInput = "";
            int cursorPosition = 0;
            int historyIndex = history.Count;
            int startLeft = Console.CursorLeft;
            int startTop = Console.CursorTop;

            while (true)
            {
                var keyInfo = Console.ReadKey(true);

                if (keyInfo.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    break;
                }
                else if (keyInfo.Key == ConsoleKey.Backspace)
                {
                    if (cursorPosition > 0 && currentInput.Length > 0)
                    {
                        currentInput = currentInput.Remove(cursorPosition - 1, 1);
                        cursorPosition--;
                        RefreshLine(startLeft, startTop, currentInput, cursorPosition);
                    }
                }
                else if (keyInfo.Key == ConsoleKey.Delete)
                {
                    if (cursorPosition < currentInput.Length)
                    {
                        currentInput = currentInput.Remove(cursorPosition, 1);
                        RefreshLine(startLeft, startTop, currentInput, cursorPosition);
                    }
                }
                else if (keyInfo.Key == ConsoleKey.LeftArrow)
                {
                    if (cursorPosition > 0)
                    {
                        cursorPosition--;
                        SetConsoleCursor(startLeft, startTop, cursorPosition);
                    }
                }
                else if (keyInfo.Key == ConsoleKey.RightArrow)
                {
                    if (cursorPosition < currentInput.Length)
                    {
                        cursorPosition++;
                        SetConsoleCursor(startLeft, startTop, cursorPosition);
                    }
                }
                else if (keyInfo.Key == ConsoleKey.UpArrow)
                {
                    if (history.Count > 0 && historyIndex > 0)
                    {
                        historyIndex--;
                        currentInput = history[historyIndex];
                        cursorPosition = currentInput.Length;
                        RefreshLine(startLeft, startTop, currentInput, cursorPosition);
                    }
                }
                else if (keyInfo.Key == ConsoleKey.DownArrow)
                {
                    if (history.Count > 0 && historyIndex < history.Count - 1)
                    {
                        historyIndex++;
                        currentInput = history[historyIndex];
                        cursorPosition = currentInput.Length;
                        RefreshLine(startLeft, startTop, currentInput, cursorPosition);
                    }
                    else if (historyIndex >= history.Count - 1)
                    {
                        historyIndex = history.Count;
                        currentInput = "";
                        cursorPosition = 0;
                        RefreshLine(startLeft, startTop, currentInput, cursorPosition);
                    }
                }
                else if (keyInfo.Key == ConsoleKey.Tab)
                {
                    ShellCompletionResult result = ShellCompletion.Complete(currentInput, cursorPosition, path);
                    if (result.Changed)
                    {
                        currentInput = result.Text;
                        cursorPosition = result.CursorPosition;
                        RefreshLine(startLeft, startTop, currentInput, cursorPosition);
                    }
                    else if (result.Matches.Length > 1)
                    {
                        SetConsoleCursor(startLeft, startTop, currentInput.Length);
                        Console.WriteLine();
                        Console.WriteLine(string.Join("  ", result.Matches));
                        Console.Write(prompt);
                        startLeft = Console.CursorLeft;
                        startTop = Console.CursorTop;
                        RefreshLine(startLeft, startTop, currentInput, cursorPosition);
                    }
                }
                else if (keyInfo.KeyChar >= 32 && keyInfo.KeyChar <= 126)
                {
                    currentInput = currentInput.Insert(cursorPosition, keyInfo.KeyChar.ToString());
                    cursorPosition++;
                    RefreshLine(startLeft, startTop, currentInput, cursorPosition);
                }
            }

            return currentInput;
        }

        private void RefreshLine(int startLeft, int startTop, string currentInput, int cursorPosition)
        {
            try
            {
                Console.SetCursorPosition(startLeft, startTop);
                Console.Write(currentInput + " ");
                SetConsoleCursor(startLeft, startTop, cursorPosition);
            }
            catch
            {
            }
        }

        private void SetConsoleCursor(int startLeft, int startTop, int cursorPosition)
        {
            try
            {
                int targetLeft = startLeft + cursorPosition;
                int windowWidth = 80;
                try
                {
                    windowWidth = Console.WindowWidth;
                }
                catch
                {
                }

                if (windowWidth <= 0)
                    windowWidth = 80;
                int targetTop = startTop + (targetLeft / windowWidth);
                targetLeft %= windowWidth;
                Console.SetCursorPosition(targetLeft, targetTop);
            }
            catch
            {
            }
        }
    }
}
