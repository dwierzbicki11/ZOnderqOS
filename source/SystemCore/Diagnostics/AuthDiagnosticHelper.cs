namespace ZonderqOS.SystemCore.Diagnostics
{
    public static class AuthDiagnosticHelper
    {
        // A real GUI Lock/Unlock regression must be driven by a separate
        // interactive harness. Creating persistent users in /etc/passwd and
        // /etc/shadow is forbidden in a physical-device diagnostic.
        public static (bool ok, string reason, string metrics) RunIsolatedAuthTest()
        {
            return (false,
                "Nie wykonywano rzeczywistych cykli GUI Lock/Unlock; konta pozostaly nietkniete",
                "auth_mutations=0;lock_cycles=NOT_TESTED;wrong_attempts=NOT_TESTED");
        }
    }
}
