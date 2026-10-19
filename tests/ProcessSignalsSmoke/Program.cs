using System;
using System.Collections.Generic;
using System.Threading;
using ZonderqOS;
using ZonderqOS.Commands;
using ZonderqOS.SystemCore;

internal static class Program
{
    private static int Main()
    {
        try
        {
            CheckParsing();
            CheckInvalidCommands();
            CheckCancelRunsOutsideRegistryLock();
            CheckShellCommandsAndStatus();
            Console.WriteLine("[PROCESS-SIGNALS] PASS");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("[PROCESS-SIGNALS] FAIL: " + error);
            return 1;
        }
    }

    private static void CheckParsing()
    {
        Check(ProcessSignals.TryParse("TERM", out ProcessSignal term) &&
            term == ProcessSignal.Terminate, "TERM parser failed");
        Check(ProcessSignals.TryParse("-SIGint", out ProcessSignal interrupt) &&
            interrupt == ProcessSignal.Interrupt, "-SIGint parser failed");
        Check(ProcessSignals.TryParse("15", out term) &&
            term == ProcessSignal.Terminate, "15 parser failed");
        Check(ProcessSignals.TryParse("-2", out interrupt) &&
            interrupt == ProcessSignal.Interrupt, "-2 parser failed");
        Check(!ProcessSignals.TryParse("-9", out _) &&
            !ProcessSignals.TryParse("SIGKILL", out _) &&
            !ProcessSignals.TryParse(null, out _), "unsupported signal accepted");
        Check(!ProcessSignals.IsSupported((ProcessSignal)9), "SIGKILL unexpectedly supported");
    }

    private static void CheckInvalidCommands()
    {
        string path = "/";
        var kill = new CmdKill();
        CommandIO.Clear();
        kill.Execute(new[] { "kill", "-l" }, ref path);
        Check(CommandIO.LastCommandSuccess && CommandIO.OutputContains("SIGKILL"),
            "kill -l should document unsupported SIGKILL");
        CommandIO.Clear();
        kill.Execute(new[] { "kill", "-9", "5" }, ref path);
        Check(!CommandIO.LastCommandSuccess && CommandIO.OutputContains("not supported"),
            "kill -9 must fail closed");
        CommandIO.Clear();
        kill.Execute(new[] { "kill", "0" }, ref path);
        Check(!CommandIO.LastCommandSuccess, "PID 0 accepted");
        CommandIO.Clear();
        kill.Execute(new[] { "kill", "15", "extra" }, ref path);
        Check(!CommandIO.LastCommandSuccess, "malformed kill accepted");
        Check(!ProcessManager.SendSignal(999999, ProcessSignal.Interrupt),
            "missing PID was signaled");
    }

    private static void CheckCancelRunsOutsideRegistryLock()
    {
        using var ready = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        int callbackWasUnlocked = 0;

        int pid = ProcessManager.Start("callback-lock-smoke", token =>
        {
            token.Register(() =>
            {
                // An actual deadlock requires two different threads: Monitor locks
                // are reentrant on the SAME thread. The callback waits for a second
                // thread to acquire ProcessManager's registry lock.
                using var registryVisited = new ManualResetEventSlim(false);
                Thread inspector = new Thread(() =>
                {
                    ProcessManager.GetActiveProcesses();
                    registryVisited.Set();
                });
                inspector.IsBackground = true;
                inspector.Start();
                Volatile.Write(ref callbackWasUnlocked, registryVisited.Wait(2000) ? 1 : -1);
            });

            ready.Set();
            while (!token.IsCancellationRequested)
                Thread.Sleep(2);
            release.Wait(4000);
        });

        try
        {
            Check(ready.Wait(3000), "process was not scheduled");
            Check(ProcessManager.SendSignal(pid, ProcessSignal.Interrupt),
                "SIGINT was not delivered");
            Check(Volatile.Read(ref callbackWasUnlocked) == 1,
                "cancel callback blocked another thread on the process registry lock");
            KernelProcess process = Find(pid);
            Check(process != null && process.LastSignalNumber == 2, "last signal was not recorded");
            Check(ProcessManager.Kill(pid), "legacy Kill is not idempotent SIGTERM");
            Check(Find(pid)?.LastSignalNumber == 15, "Kill did not record SIGTERM");
        }
        finally
        {
            release.Set();
            WaitUntil(() => Find(pid) == null, "process cleanup did not finish");
        }
    }

    private static void CheckShellCommandsAndStatus()
    {
        using var ready1 = new ManualResetEventSlim(false);
        using var ready2 = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        int pid1 = StartParked("signal-shell-smoke", ready1, release);
        int pid2 = StartParked("signal-shell-smoke", ready2, release);

        try
        {
            Check(ready1.Wait(3000) && ready2.Wait(3000), "shell test processes did not start");
            string path = "/";
            var kill = new CmdKill();
            var killall = new CmdKillall();
            var ps = new CmdPs();

            CommandIO.Clear();
            kill.Execute(new[] { "kill", "-INT", pid1.ToString() }, ref path);
            Check(CommandIO.LastCommandSuccess && CommandIO.OutputContains("SIGINT"),
                "kill -INT <pid> failed");
            Check(Find(pid1)?.LastSignalNumber == 2, "SIGINT not reflected in registry");

            CommandIO.Clear();
            ps.Execute(new[] { "ps" }, ref path);
            Check(CommandIO.LastCommandSuccess &&
                CommandIO.OutputContains("SIGNAL") && CommandIO.OutputContains(pid1.ToString()),
                "ps missing signal column");

            CommandIO.Clear();
            killall.Execute(new[] { "killall", "-s", "TERM", "signal-shell-smoke" }, ref path);
            Check(CommandIO.LastCommandSuccess && CommandIO.OutputContains("SIGTERM"),
                "killall -s TERM failed");
            Check(Find(pid1)?.LastSignalNumber == 15 && Find(pid2)?.LastSignalNumber == 15,
                "killall did not reach all matching processes");
            CommandIO.Clear();
            killall.Execute(new[] { "killall", "-s", "KILL", "signal-shell-smoke" }, ref path);
            Check(!CommandIO.LastCommandSuccess, "killall SIGKILL should be refused");
        }
        finally
        {
            release.Set();
            WaitUntil(() => Find(pid1) == null && Find(pid2) == null,
                "shell test process cleanup did not finish");
        }
    }

    private static int StartParked(string name, ManualResetEventSlim ready,
        ManualResetEventSlim release)
    {
        return ProcessManager.Start(name, token =>
        {
            ready.Set();
            while (!token.IsCancellationRequested)
                Thread.Sleep(2);
            release.Wait(4000);
        });
    }

    private static KernelProcess Find(int pid)
    {
        List<KernelProcess> processes = ProcessManager.GetActiveProcesses();
        foreach (KernelProcess item in processes)
            if (item.PID == pid)
                return item;
        return null;
    }

    private static void WaitUntil(Func<bool> condition, string message)
    {
        for (int i = 0; i < 300; i++)
        {
            if (condition())
                return;
            Thread.Sleep(10);
        }
        throw new InvalidOperationException(message);
    }

    private static void Check(bool result, string message)
    {
        if (!result)
            throw new InvalidOperationException(message);
    }
}

namespace ZonderqOS
{
    public static class WriteMessage
    {
        public static void WriteError(string message, string category) { }
    }

    public static class CommandIO
    {
        public static bool LastCommandSuccess;
        private static readonly List<string> Output = new List<string>();
        public static void Clear()
        {
            Output.Clear();
            LastCommandSuccess = false;
        }
        public static void WriteLine(string message) => Output.Add(message);
        public static bool OutputContains(string text)
        {
            foreach (string line in Output)
                if (line.Contains(text, StringComparison.Ordinal))
                    return true;
            return false;
        }
    }
}

namespace ZonderqOS.SystemCore
{
    internal static class SchedulerTelemetry
    {
        internal static uint CurrentThreadId() => 1u;
        internal static void EnableCurrentManagedPreemption() { }
    }
}
