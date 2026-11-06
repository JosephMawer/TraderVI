import json
import tempfile
import unittest
from pathlib import Path

import check_consolidation as consolidation


class ConsolidationChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "Docs").mkdir()

    def registry(self, entries):
        (self.root / consolidation.REGISTRY).write_text(
            json.dumps({"version": 1, "documents": entries}), encoding="utf-8"
        )

    def test_unreviewed_changed_document_is_rejected_until_registered(self):
        (self.root / "Docs/example.md").write_text("# Example\n", encoding="utf-8")
        self.registry([])
        registered, errors = consolidation.validate_registry(self.root)
        self.assertEqual([], errors)
        self.assertEqual(1, len(consolidation.check_changed(registered, ["Docs/example.md"])))
        self.registry([{"path": "Docs/example.md", "reviewed": "2026-11-05"}])
        registered, errors = consolidation.validate_registry(self.root)
        self.assertEqual([], errors)
        self.assertEqual([], consolidation.check_changed(registered, ["Docs/example.md"]))

    def test_invalid_registry_records_fail(self):
        (self.root / "Docs/example.md").write_text("# Example\n", encoding="utf-8")
        self.registry([
            {"path": "Docs/example.md", "reviewed": "2026-13-01"},
            {"path": "Docs/example.md", "reviewed": "2026-11-05"},
            {"path": "Docs/missing.md", "reviewed": "2026-11-05"},
            {"path": "../outside.md", "reviewed": "2026-11-05"},
        ])
        _, errors = consolidation.validate_registry(self.root)
        self.assertEqual(4, len(errors))
        self.assertTrue(any("Duplicate" in error for error in errors))
        self.assertTrue(any("missing" in error for error in errors))

    def test_malformed_registry_fails_closed(self):
        (self.root / consolidation.REGISTRY).write_text("not json", encoding="utf-8")
        _, errors = consolidation.validate_registry(self.root)
        self.assertEqual(1, len(errors))

    def test_scope_includes_documentation_but_not_text_configuration(self):
        self.assertTrue(consolidation.is_documentation("Docs/example.MD"))
        self.assertTrue(consolidation.is_documentation("Delphi/Notes.txt"))
        self.assertTrue(consolidation.is_documentation("Docs/future.txt"))
        self.assertFalse(consolidation.is_documentation("blacklist.txt"))


if __name__ == "__main__":
    unittest.main()
