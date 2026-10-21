#!/usr/bin/env python3
"""Validate that compile-only CI can create Roslyn's generated-source output."""
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
project = ET.parse(ROOT / "ZonderqOS.csproj").getroot()
assert project.attrib.get("Sdk") == "Cosmos.Sdk/3.0.94"
required = (
    "Cosmos.Kernel.SourceGenerators.CosmosEntryPointGenerator",
    "Cosmos.Kernel.SourceGenerators.DriverManifestGenerator",
    "Microsoft.Interop.LibraryImportGenerator",
)
target = next((x for x in project.findall("Target") if x.get("Name") == "PrepareRoslynGeneratedOutput"), None)
assert target is not None, "CS0016: missing source-generator output directory preparation target"
assert target.get("BeforeTargets") == "CoreCompile"
dirs = ";".join(x.get("Directories", "") for x in target.findall("MakeDir"))
for name in required:
    assert name in dirs, f"missing Roslyn generated-source directory: {name}"
print("Roslyn generated-source directory contract: PASS")
