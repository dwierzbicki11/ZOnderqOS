"""Production sysbench safety contracts: host static checks, not guest E2E."""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
read = lambda path: (ROOT / path).read_text(encoding="utf-8")

def test_sysbench_has_no_persistent_test_accounts():
    auth = read("source/SystemCore/Diagnostics/AuthDiagnosticHelper.cs")
    assert "CreateUser(" not in auth
    assert "ValidateCredentials(" not in auth
    assert "CheckSessionReadOnly()" in auth
    assert "_sysbench_tmp_user" not in auth

def test_sysbench_writes_only_bound_approved_root():
    disk = read("source/Storage/Disk.cs")
    fat = read("source/SystemCore/Diagnostics/Fat32DiagnosticHelper.cs")
    assert "IsApprovedPersistentRootMounted" in disk
    assert "DataVolumePolicy.TryFindDataPartition" in disk
    assert 'rootPath = "/";' in fat
    assert "Disk.IsApprovedPersistentRootMounted" in fat
    assert "FileMode.CreateNew" in fat
    assert 'rootPath != "/"' in fat
    assert 'FileMode.Create,' not in fat
    assert r'@"0:\\", @"1:\\"' not in fat

def test_sysbench_no_fabricated_pass():
    runner = read("source/SystemCore/Diagnostics/SystemDiagnosticRunner.cs")
    assert "RunAppCycleTest()" in runner
    assert 'Name = "100 GUI open/close cycles"' in runner
    assert "Status = DiagnosticStatus.NotTested" in runner
    assert "soakRealSeconds[4] >= 3600" in runner
    assert 'Name = "Reboot resume"' in runner
    assert 'Metrics = "reboots=0"' in runner

def test_export_to_stable_usb_root_not_host_disk_index():
    runner = read("source/SystemCore/Diagnostics/SystemDiagnosticRunner.cs")
    assert 'Path.Combine(root, "sysbench_report.md")' in runner
    assert 'Path.Combine(root, "sysbench_report.csv")' in runner
    assert 'Path.Combine(root, "sysbench_report.json")' in runner
    assert 'root != "/"' in runner
    assert 'SecurityContext.CurrentUid != 0' in runner
    assert 'LastReportPath = "NOT_SAVED' in runner

def test_gc_remains_untouched():
    for path in (
        "source/SystemCore/Diagnostics/SystemDiagnosticRunner.cs",
        "source/SystemCore/Diagnostics/Fat32DiagnosticHelper.cs",
        "source/SystemCore/Diagnostics/AuthDiagnosticHelper.cs",
        "source/Shell/Commands/CmdSysbench.cs",
    ):
        assert "GC.Collect(" not in read(path)
