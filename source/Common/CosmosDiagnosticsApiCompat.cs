// Cosmos v3.0.94 renamed the public scheduler/memory diagnostic views.
// Keep ZonderqOS monitors on the official 3.0.94 API, not fabricated data.
// The stable 3.0.85 production build still uses the original names.
#if ZONDERQ_COSMOS_INPUT_API
global using SchedulerInfo = Cosmos.Kernel.System.Diagnostics.SchedulerDiagnostics;
global using MemoryInfo = Cosmos.Kernel.System.Diagnostics.MemoryDiagnostics;
#endif
