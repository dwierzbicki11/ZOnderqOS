import pathlib


ROOT = pathlib.Path(__file__).resolve().parents[1]


def test_sysbench_files_exist():
    assert (ROOT / "source/SystemCore/Diagnostics/SystemDiagnosticRunner.cs").is_file()
    assert (ROOT / "source/SystemCore/Diagnostics/Fat32DiagnosticHelper.cs").is_file()
    assert (ROOT / "source/SystemCore/Diagnostics/AuthDiagnosticHelper.cs").is_file()
    assert (ROOT / "source/Shell/Commands/CmdSysbench.cs").is_file()


def test_no_gc_collect_in_diagnostics():
    for rel in [
        "source/SystemCore/Diagnostics/SystemDiagnosticRunner.cs",
        "source/SystemCore/Diagnostics/Fat32DiagnosticHelper.cs",
        "source/SystemCore/Diagnostics/AuthDiagnosticHelper.cs",
        "source/Shell/Commands/CmdSysbench.cs",
    ]:
        text = (ROOT / rel).read_text(encoding="utf-8")
        assert "GC.Collect" not in text, f"Forbidden GC.Collect in {rel}"


def test_sysbench_registered_in_command_cs():
    cmd_cs = (ROOT / "source/Shell/Command.cs").read_text(encoding="utf-8")
    assert "Register(new CmdSysbench());" in cmd_cs


def test_gui_diagnostics_tab_wired():
    gui_cs = (ROOT / "source/GUI/Application/SysBenchmarkApp.cs").read_text(encoding="utf-8")
    assert "SystemDiagnosticRunner.Shared" in gui_cs
    assert "Rozpocznij pelny test" in gui_cs
    assert "Przerwij" in gui_cs
