#!/usr/bin/env python3
"""Read-only branch triage against Cosmos 3.0.94 main."""
import argparse
import collections
import datetime
import json
from pathlib import Path
import subprocess


def g(*cmd, check=True):
    return subprocess.run(["git", *cmd], text=True, capture_output=True, check=check)


def one(*cmd):
    return g(*cmd).stdout.strip()


def audit(main, name, prs):
    ref = "refs/remotes/origin/" + name
    behind, ahead = map(int, one("rev-list", "--left-right", "--count", main + "..." + ref).split())
    main_tree = one("rev-parse", main + "^{tree}")
    branch_tree = one("rev-parse", ref + "^{tree}")
    project = g("show", ref + ":ZonderqOS.csproj", check=False).stdout
    if g("merge-base", "--is-ancestor", ref, main, check=False).returncode == 0:
        state, modified = "HISTORY_IN_MAIN", []
    elif main_tree == branch_tree:
        state, modified = "SAME_TREE", []
    else:
        result = g("merge-tree", "--write-tree", main, ref, check=False)
        if result.returncode:
            state = "CONFLICT"
            modified = one("diff", "--name-only", main, ref).splitlines()
        else:
            proposed = result.stdout.splitlines()[0]
            modified = one("diff", "--name-only", main_tree, proposed).splitlines()
            state = "NEEDS_PORT_AND_TEST" if modified else "MERGE_NO_CHANGE"
    unsafe = ("Kernel.cs", "ZonderqOS.csproj", "NuGet.Config", "Directory.Build.", "global.json", "Bootloader/",
              ".github/workflows/", "patches/", "source/Core/", "source/Drivers/",
              "source/Processes/", "source/Security/", "source/Storage/")
    if "Cosmos.Sdk/3.0.94" not in project:
        risk = "LEGACY_SDK"
    elif any(f.startswith(prefix) for f in modified for prefix in unsafe):
        risk = "KERNEL_OR_BUILD"
    elif any(w in name.lower() for w in ("smp", "smt", "arm64", "network", "driver")):
        risk = "LOW_LEVEL"
    else:
        risk = "APP_OR_TOOL"
    return dict(branch=name, state=state, risk=risk, behind=behind,
                ahead=ahead, file_count=len(modified), sample=modified[:8],
                pull_requests=prs.get(name, []))


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--markdown", required=True)
    p.add_argument("--json", required=True)
    p.add_argument("--prs")
    a = p.parse_args()
    mainref = "refs/remotes/origin/main"
    sha = one("rev-parse", mainref)
    if "Cosmos.Sdk/3.0.94" not in one("show", mainref + ":ZonderqOS.csproj"):
        raise SystemExit("Refuse inventory: main SDK is not 3.0.94")
    prs = {}
    if a.prs and Path(a.prs).exists():
        for item in json.loads(Path(a.prs).read_text()):
            prs.setdefault(item["headRefName"], []).append(
                {"number": item["number"], "url": item["url"],
                 "draft": item.get("isDraft", False)})
    refs = one("for-each-ref", "--format=%(refname:lstrip=3)",
               "refs/remotes/origin").splitlines()
    names = sorted(b for b in refs if b not in ("HEAD", "main"))
    rows = [audit(mainref, n, prs) for n in names]
    counts = collections.Counter(r["state"] for r in rows)
    md = [
        "# ZonderqOS — branch inventory",
        "",
        "Generated UTC: " + datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "main commit: " + sha,
        "Canonical SDK/runtime: Cosmos 3.0.94 (no downgrade).",
        "",
        "This is a read-only inventory. It does not merge, delete, or certify SMP.",
        "",
        "## Summary",
        "",
        "Non-main branches: " + str(len(rows)),
        "",
    ]
    md += ["- " + k + ": " + str(v) for k, v in sorted(counts.items())]
    md += ["", "## Classification", "",
           "- HISTORY_IN_MAIN: branch tip already reachable from main",
           "- SAME_TREE: all tracked content matches main, but history differs",
           "- MERGE_NO_CHANGE: clean merge changes no main files",
           "- NEEDS_PORT_AND_TEST: would change main; requires gated tests",
           "- CONFLICT: cannot merge automatically; selective port required",
           "",
           "## Remaining branches", "",
           "| Branch | State | Risk | Behind / Ahead | Files | PR |",
           "|---|---|---|---:|---:|---|"]
    ranks = {"NEEDS_PORT_AND_TEST": 0, "CONFLICT": 1, "MERGE_NO_CHANGE": 2,
             "SAME_TREE": 3, "HISTORY_IN_MAIN": 4}
    for r in sorted(rows, key=lambda x: (ranks[x["state"]], x["branch"])):
        prs_link = ", ".join("[#" + str(pr["number"]) + "](" + pr["url"] + ")"
                             for pr in r["pull_requests"])
        md.append("| " + r["branch"] + " | " + r["state"] + " | " +
                  r["risk"] + " | " + str(r["behind"]) + " / " +
                  str(r["ahead"]) + " | " + str(r["file_count"]) +
                  " | " + prs_link + " |")
    md += ["", "## Largest pending deltas", ""]
    for r in sorted(rows, key=lambda x: -x["file_count"])[:30]:
        md.append("- " + r["branch"] + ": " + ", ".join(r["sample"]))
    md += ["", "## Safety gates", "",
           "Do not automatically merge changes touching the kernel, managed SMP, "
           "OrionGC, disks, security, network or hardware drivers. Require "
           "matching Cosmos 3.0.94 packages, builds, QEMU tests and relevant "
           "physical validation. Do not mistake contract tests for hardware proof.",
           ""]
    Path(a.markdown).parent.mkdir(parents=True, exist_ok=True)
    Path(a.markdown).write_text("\n".join(md))
    Path(a.json).write_text(json.dumps(
        {"main": sha, "sdk": "3.0.94", "branches": rows},
        ensure_ascii=False, indent=2) + "\n")
    for k, v in sorted(counts.items()):
        print("[INVENTORY]", k, v)
    print("[INVENTORY] Total:", len(rows))


if __name__ == "__main__":
    main()
