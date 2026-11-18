#!/usr/bin/env python3
"""Secondary probabilistic Polish-language scan, for manual review only."""
from pathlib import Path
import re
from collections import Counter
from wordfreq import zipf_frequency

root=Path(__file__).resolve().parents[1]
pat=re.compile(r'"(?:\\.|[^"\\\n])*"')
tokpat=re.compile(r"[A-Za-ząćęłńóśźżĄĆĘŁŃÓŚŹŻ]{3,}")
exclude={"cosmos","zonderqos","limine","vfs","smp","intel","amd","efi","fat","qemu","sha","gcc","gui","hmac","api","localhost"}
found=[]
for path in [root/"Kernel.cs",*sorted((root/"source").rglob("*.cs"))]:
    if path.name == "LocalizationManager.cs":
        continue
    content=path.read_text(encoding="utf-8",errors="replace")
    for lineno,line in enumerate(content.splitlines(),1):
        if line.lstrip().startswith(("//","/*","*")):
            continue
        for m in pat.finditer(line):
            lit=m.group(0)
            if "/root/Co_Nowego_W_Aktualizacji" in lit or lit == '"centrum aplik"':
                continue
            scores=[]
            for word in tokpat.findall(lit):
                w=word.lower()
                if w in exclude or len(w)<4:continue
                pl=zipf_frequency(w,"pl")
                en=zipf_frequency(w,"en")
                if pl>=3.0 and pl-en>=1.15:
                    scores.append((w,round(pl-en,1),round(pl,1),round(en,1)))
            if scores:
                found.append((str(path.relative_to(root)),lineno,lit[:180],scores))
print("SUSPECT_POLISH_COUNT",len(found),"FILES",len(set(x[0] for x in found)))
for path,ln,lit,words in found[:350]:
    print(f"LANG_SUSPECT {path}:{ln} {lit!r} WORDS {words!r}")
