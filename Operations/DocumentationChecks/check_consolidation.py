"""Validate the review registry and changed tracked documentation files.

The registry is an attestation of a completed human/agent review, not a content
quality or freshness check. Stage new files before comparing with a base.
"""
import argparse
from datetime import date
import json
from pathlib import Path, PurePosixPath
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
REGISTRY = Path("Docs/documentation-consolidation.json")
TEXT_DOCUMENTS = {
    "Core/TMX/notes.txt", "Delphi/Notes.txt", "TODO.txt", "TraderVI.txt", "Useful links.txt",
}


def validate_registry(root):
    errors = []
    try:
        registry = json.loads((root / REGISTRY).read_text(encoding="utf-8-sig"))
    except (OSError, ValueError) as exc:
        return set(), [f"Cannot read documentation registry: {exc}"]
    if not isinstance(registry, dict) or registry.get("version") != 1 or not isinstance(registry.get("documents"), list):
        return set(), ["Documentation registry must have version 1 and a documents list."]

    registered = set()
    for entry in registry["documents"]:
        if not isinstance(entry, dict) or set(entry) != {"path", "reviewed"}:
            errors.append("Each registry entry must contain only path and reviewed.")
            continue
        name, reviewed = entry["path"], entry["reviewed"]
        if not isinstance(name, str) or not name or "\\" in name:
            errors.append(f"Invalid documentation path: {name!r}")
            continue
        path = PurePosixPath(name)
        if path.is_absolute() or ".." in path.parts or path.as_posix() != name or path.suffix.lower() not in {".md", ".txt"}:
            errors.append(f"Invalid documentation path: {name!r}")
            continue
        if name in registered:
            errors.append(f"Duplicate documentation registry entry: {name}")
        registered.add(name)
        if not (root / name).is_file():
            errors.append(f"Registered documentation is missing: {name}")
        try:
            if not isinstance(reviewed, str) or date.fromisoformat(reviewed).isoformat() != reviewed:
                raise ValueError()
        except ValueError:
            errors.append(f"Invalid review date for {name}: {reviewed!r}")
    return registered, errors


def check_changed(registered, changed):
    return [f"Documentation requires its initial consolidation review: {name}"
            for name in sorted(set(changed) - registered)]


def is_documentation(name):
    return (name.lower().endswith(".md") or name in TEXT_DOCUMENTS
            or (name.startswith("Docs/") and name.lower().endswith(".txt")))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", help="Git revision to compare with the working tree; omit to validate only the registry")
    args = parser.parse_args()
    changed = []
    if args.base:
        try:
            changed = subprocess.check_output(
                ["git", "diff", "--name-only", "--diff-filter=ACMRT", "-z", args.base],
                cwd=ROOT,
            ).decode("utf-8").split("\0")
        except (subprocess.CalledProcessError, UnicodeDecodeError) as exc:
            print(f"Cannot determine changed documentation: {exc}", file=sys.stderr)
            return 1
        changed = [name for name in changed if is_documentation(name)]
    registered, errors = validate_registry(ROOT)
    errors.extend(check_changed(registered, changed))
    if errors:
        print("\n".join(errors), file=sys.stderr)
    print(f"Checked {len(changed)} changed documentation files and {len(registered)} review records; {len(errors)} errors.")
    return int(bool(errors))


if __name__ == "__main__":
    sys.exit(main())
