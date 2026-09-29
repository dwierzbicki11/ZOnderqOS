#if SERVICE_CONFIG_SMOKE
using System;
using ZonderqOS.SystemCore.Services;

internal static class ServiceConfigSmoke
{
    private static int Main()
    {
        ServiceDefinition definition;
        string error;
        Check(ServiceDefinition.TryParse("my-service", "type=memory\nenabled=true\ninterval_seconds=30\n",
            out definition, out error), error);
        Check(definition.Name == "my-service" && definition.Enabled && definition.IntervalSeconds == 30,
            "Parsed service differs from configuration.");
        Reject("../escape", "type=memory\n");
        Reject("example", "type=shell\nenabled=true\n");
        Reject("example", "type=memory\ninterval_seconds=0\n");
        Reject("example", "type=memory\nenabled=true\nenabled=false\n");
        Reject("example", "type=memory\ncommand=rm\n");
        Reject("example", "type=memory\n" + new string('#', 2049));
        string updated;
        Check(ServiceDefinition.TrySetEnabled("example", "# keep me\ntype=heartbeat\nenabled=false\n",
            true, out updated, out error), error);
        Check(updated.Contains("# keep me") && updated.Contains("enabled=true") &&
            !updated.Contains("enabled=false"), "Enable did not preserve and update configuration.");
        Check(ServiceDefinition.TrySetEnabled("example", "type=heartbeat\n", false,
            out updated, out error) && updated.Contains("enabled=false"),
            "Missing enabled setting was not added.");
        Check(!ServiceDefinition.TrySetEnabled("example", "type=shell\n", true,
            out updated, out error), "Invalid service was enabled.");
        Console.WriteLine("[SERVICE-CONFIG] PASS");
        return 0;
    }

    private static void Reject(string name, string config)
    {
        ServiceDefinition definition;
        string error;
        Check(!ServiceDefinition.TryParse(name, config, out definition, out error),
            "Invalid service configuration accepted.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
#endif
