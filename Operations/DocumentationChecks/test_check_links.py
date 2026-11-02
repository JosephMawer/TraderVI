import tempfile
import unittest
from pathlib import Path

import check_links


class LinkChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        (self.root / "Docs").mkdir()

    def write(self, name, contents):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(contents, encoding="utf-8")
        return path

    def test_heading_anchors_ignore_code_and_number_duplicates(self):
        text = "# Topic — Detail\n# Topic — Detail\n```md\n# Hidden\n```\n"
        self.assertEqual({"topic--detail", "topic--detail-1"}, check_links.anchors(text))

    def test_checks_incoming_and_outgoing_links_and_reports_failures(self):
        status = self.write("Docs/project-status.md", "# Status\n[Road](roadmap.md#current)\n[Missing](missing.md)\n")
        roadmap = self.write("Docs/roadmap.md", "# Current\n")
        history = self.write("Docs/roadmap-history-20261201.md", "# Old item\n")
        other = self.write("other.md", "[Old](Docs/roadmap-history-20261201.md#old-item)\n[Bad](Docs/project-status.md#gone)\n[Unrelated](missing.md)\n`[Example](Docs/roadmap.md#missing)`\n```md\n[Example](Docs/roadmap.md#missing)\n```\n")
        count, errors = check_links.check(self.root, [status, roadmap, history, other])
        self.assertEqual(4, count)
        self.assertEqual(2, len(errors))
        self.assertTrue(any("missing file" in error for error in errors))
        self.assertTrue(any("missing anchor" in error for error in errors))

    def test_missing_history_target_is_reported(self):
        source = self.write("other.md", "[Moved](Docs/project-status-history-20261201.md#old)\n")
        count, errors = check_links.check(self.root, [source])
        self.assertEqual(1, count)
        self.assertEqual(1, len(errors))


if __name__ == "__main__":
    unittest.main()
