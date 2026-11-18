"""Alpha 1 sysbench safety contracts; run without Cosmos SDK or physical disks."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
DIAG = ROOT / "source/SystemCore/Diagnostics"
GUI = ROOT / "source/GUI/Application/SysBenchmarkApp.cs"


class SysbenchSafetyContracts(unittest.TestCase):
    def load(self, path):
        return path.read_text(encoding="utf-8")

    def test_auth_does_not_create_or_mutate_accounts(self):
        source = self.load(DIAG / "AuthDiagnosticHelper.cs")
        self.assertNotIn("UserManager.CreateUser(", source)
        self.assertNotIn("UserManager.ValidateCredentials(", source)
        self.assertIn("auth_mutations=0", source)
        self.assertIn("NOT_TESTED", source)

    def test_diagnostic_statuses_never_falsely_pass(self):
        source = self.load(DIAG / "SystemDiagnosticRunner.cs")
        self.assertIn("simulated_iterations=", source)
        self.assertIn("Status = DiagnosticStatus.NotTested,", source)
        self.assertNotIn("includeSoak60Min ? DiagnosticStatus.Pass", source)
        self.assertNotIn("includeRebootTest ? DiagnosticStatus.Pass", source)
        self.assertIn("Report status: observed tests only", source)

    def test_fat32_disk_identity_is_mounted_system_root(self):
        source = self.load(DIAG / "Fat32DiagnosticHelper.cs")
        self.assertIn('rootPath = "/"', source)
        self.assertIn('Disk.IsVerifiedPersistentDataRootMounted', source)
        self.assertNotIn('File.Exists(state)', source)
        self.assertNotIn('reader.ReadLine()', source)
        disk = self.load(ROOT / "source/Storage/Disk.cs")
        self.assertIn("DataVolumePolicy.TryFindDataPartition", disk)
        self.assertIn("_verifiedPersistentDataRootMounted = true", disk)
        self.assertIn("public static bool IsVerifiedPersistentDataRootMounted", disk)
        self.assertNotIn('@"0:', source)
        self.assertNotIn('@"1:', source)
        self.assertIn("FileMode.CreateNew", source)
        self.assertIn("fileCreated && File.Exists(testFile)", source)
        self.assertIn("directoryCreated && Directory.Exists(diagDir)", source)

    def test_export_report_contains_boot_metadata(self):
        source = self.load(DIAG / "SystemDiagnosticRunner.cs")
        self.assertIn('sysbench_report.md', source)
        self.assertIn('sysbench_report.csv', source)
        self.assertIn('sysbench_report.json', source)
        self.assertIn('BOOTGUARD_PREBOOT config:', source)
        self.assertIn('not EFI execution attestation', source)
        self.assertIn('REPORT_WRITE_FAILED', source)
        self.assertIn('REPORT_NOT_WRITTEN', source)

    def test_gui_storage_benchmark_does_not_truncate_or_fake_speed(self):
        source = self.load(GUI)
        start = source.index("private void RunRealVfsBenchmark()")
        end = source.index("private void CalculateTotalScore()", start)
        body = source[start:end]
        self.assertIn("FileMode.CreateNew", body)
        self.assertNotIn("FileMode.Create,", body)
        self.assertIn("if (File.Exists(testPath))", body)
        self.assertNotIn("vfsReadSpeedKb = 2500", body)

    def test_sysbench_terminal_and_report_status_are_real(self):
        command = self.load(ROOT / "source/Shell/Commands/CmdSysbench.cs")
        self.assertIn("CommandIO.WriteLine", command)
        self.assertNotIn("Console.WriteLine", command)
        self.assertIn('runner.LastReportPath == "/sysbench_report.md"', command)
        self.assertIn("CommandIO.LastCommandSuccess = success;", command)
        self.assertIn("Unknown option:", command)

    def test_gui_quick_label_and_current_cosmos_identity(self):
        source = self.load(GUI)
        build = self.load(ROOT / "source/Core/KernelBuildInfo.cs")
        project = self.load(ROOT / "ZonderqOS.csproj")
        self.assertIn("Rozpocznij szybki test", source)
        self.assertIn("SystemDiagnosticRunner.Shared.IsRunning;", source)
        self.assertIn('CosmosVersion = "Cosmos 3.0.97 Native Core"', build)
        self.assertIn('Sdk="Cosmos.Sdk/3.0.97"', project)
        self.assertIn("#if ZONDERQ_ARCH_ARM64", build)

    def test_no_manual_gc(self):
        files = [
            DIAG / "AuthDiagnosticHelper.cs",
            DIAG / "Fat32DiagnosticHelper.cs",
            DIAG / "SystemDiagnosticRunner.cs",
            GUI,
        ]
        for file in files:
            self.assertNotIn("GC.Collect(", self.load(file), str(file))


if __name__ == "__main__":
    unittest.main()
