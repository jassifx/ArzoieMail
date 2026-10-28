"""Read-only documentation link and legacy inventory checks; safe on the development host."""
from pathlib import Path
import re
from urllib.parse import unquote

root = Path(__file__).resolve().parents[3]
documents = sorted((root / "docs").rglob("*.md")) + [
    root / name for name in ("README.md", "ROADMAP.md", "AGENTS.md", "BUILDING.md",
                            "hmailserver/source/Management/README.md", "hmailserver/documentation/README.md")
]
errors = []
links = 0
for source in documents:
    for target in re.findall(r"\]\(([^)]+)\)", source.read_text(encoding="utf-8-sig")):
        target = target.split("#", 1)[0].strip("<>")
        if not target or ":" in target:
            continue
        links += 1
        if not (source.parent / unquote(target)).exists():
            errors.append(f"Missing link: {source.relative_to(root)} -> {target}")

ledger = (root / "docs/phase-1b-status.md").read_text(encoding="utf-8")
recorded_php = re.findall(r"^\| `([^`]+\.php)` \|", ledger, re.MULTILINE)
recorded_desktop = re.findall(r"^\| `([^`]+\.cs)` \|", ledger, re.MULTILINE)
php_root = root / "hmailserver/source/WebAdmin"
desktop_root = root / "hmailserver/source/Tools/Administrator/Main panes"
actual_php = {path.relative_to(php_root).as_posix() for path in php_root.rglob("*.php")}
actual_desktop = {path.name for path in desktop_root.glob("*.cs") if not path.name.endswith(".Designer.cs")}
for label, recorded, actual in (("PHP", recorded_php, actual_php), ("desktop pane", recorded_desktop, actual_desktop)):
    if len(recorded) != len(set(recorded)):
        errors.append(f"Duplicate {label} inventory entries")
    if set(recorded) != actual:
        errors.append(f"{label} missing={sorted(actual - set(recorded))}; stale={sorted(set(recorded) - actual)}")

ids = re.findall(r"^\| ([FAE]\d{2}) \|", ledger, re.MULTILINE)
if len(ids) != len(set(ids)):
    errors.append("Duplicate capability IDs")
for owner in re.findall(r"^\| `[^`]+\.(?:php|cs)` \| ([^|]+) \|", ledger, re.MULTILINE):
    for area in owner.strip().split("/"):
        if area not in ids:
            errors.append(f"Unknown inventory owner: {area}")

if errors:
    raise SystemExit("\n".join(errors))
print(f"Checked {len(documents)} documents, {links} local links, {len(actual_php)} PHP sources, "
      f"{len(actual_desktop)} desktop panes and {len(ids)} capability IDs.")
print("Static inventory/link coverage only; this does not validate field parity, runtime behavior, "
      "external URLs, anchors or the inherited manual's publication.")
