// Resolve the scheduler thread state explicitly for GUI task-manager code.
// TaskManagerApp also imports System.Diagnostics for Stopwatch, which contains
// another ThreadState enum. This alias keeps ThreadState bound to Cosmos Gen3.
global using ThreadState = Cosmos.Kernel.Core.Scheduler.ThreadState;
