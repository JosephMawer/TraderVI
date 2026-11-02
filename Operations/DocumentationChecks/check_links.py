"""Check inline local Markdown links in and into the current tracking records.

Standard-library only; run from any directory. External URLs, code examples, reference-style
links, raw HTML and non-Markdown destinations are outside this check.
"""
from collections import Counter
from pathlib import Path
import re
import subprocess
import sys
import unicodedata
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[2]
TRACKING = {"Docs/project-status.md", "Docs/roadmap.md"}
LINK = re.compile(r"(?<!!)\[[^\]]*\]\((<[^>]+>|[^\s)]+)(?:\s+['\"][^)]*['\"])?\)")
HEADING = re.compile(r"^ {0,3}#{1,6}\s+(.+?)\s*#*\s*$")
FENCE = re.compile(r"^ {0,3}(`{3,}|~{3,})")


def prose_lines(text):
    fence = None
    for number, line in enumerate(text.splitlines(), 1):
        match = FENCE.match(line)
        if match:
            marker = match.group(1)
            if fence is None:
                fence = marker
            elif marker[0] == fence[0] and len(marker) >= len(fence):
                fence = None
            continue
        if fence is None:
            yield number, line


def anchors(text):
    counts = Counter()
    result = set()
    for _, line in prose_lines(text):
        match = HEADING.match(line)
        if not match:
            continue
        heading = re.sub(r"!?(?:\[([^]]+)\]\([^)]+\))", r"\1", match.group(1))
        slug = "".join(c for c in heading.lower() if c in "-_ " or unicodedata.category(c)[0] in "LN").replace(" ", "-")
        count = counts[slug]
        counts[slug] += 1
        result.add(slug if count == 0 else f"{slug}-{count}")
    return result


def check(root, files):
    errors = []
    checked = 0
    tracked = {root / f for f in TRACKING}
    watched = {root / f for f in TRACKING}
    for source in files:
        try:
            text = source.read_text(encoding="utf-8-sig")
        except UnicodeDecodeError:
            continue
        for number, line in prose_lines(text):
            # Inline code is an example, not a rendered link.
            line = re.sub(r"(`+).*?\1", "", line)
            for match in LINK.finditer(line):
                url = match.group(1).strip("<>")
                parts = urlsplit(url)
                if parts.scheme or parts.netloc or url.startswith("//"):
                    continue
                target = (source.parent / unquote(parts.path)).resolve() if parts.path else source.resolve()
                history = target.parent == root / "Docs" and bool(re.fullmatch(r"(?:project-status|roadmap)-history-\d{8}\.md", target.name))
                if source.resolve() not in tracked and target not in watched and not history:
                    continue
                if target.suffix.lower() != ".md":
                    continue
                checked += 1
                if not target.is_file():
                    errors.append(f"{source.relative_to(root)}:{number}: missing file: {url}")
                elif parts.fragment and unquote(parts.fragment) not in anchors(target.read_text(encoding="utf-8-sig")):
                    errors.append(f"{source.relative_to(root)}:{number}: missing anchor: {url}")
    return checked, errors


def main():
    names = subprocess.check_output(["git", "ls-files", "--", "*.md"], cwd=ROOT, text=True).splitlines()
    checked, errors = check(ROOT, [ROOT / name for name in names if (ROOT / name).is_file()])
    if errors:
        print("\n".join(errors), file=sys.stderr)
    print(f"Checked {checked} local Markdown links in and into status/roadmap records; {len(errors)} errors.")
    return bool(errors)


if __name__ == "__main__":
    sys.exit(main())
