import copy
import unittest

from sync_planning import AGREED, DISCUSS, DEFERRED, REPO, pages, planning, reconcile


def issue(number, labels, state="open"):
    return {"number": number, "node_id": "issue-%s" % number, "state": state,
            "labels": [{"name": label} for label in labels]}


def item(number, value=None, archived=False, repo=REPO):
    return {"id": "item-%s" % number, "isArchived": archived,
            "content": {"number": number, "repository": {"nameWithOwner": repo}},
            "fieldValueByName": {"optionId": value} if value else None}


class FakeGitHub:
    def __init__(self, issues, items=()):
        self.issues = {i["number"]: copy.deepcopy(i) for i in issues}
        self.saved = copy.deepcopy(list(items))
        self.writes = []
        self.options = [{"name": s, "id": s} for s in ("Agreed", "Needs discussion", "Deferred")]
        self.live_changes = {}
        self.ignore_write = False

    def project(self):
        return {"id": "project-1"}

    def fields(self, project_id):
        return [{"id": "planning", "name": "Planning", "options": self.options},
                {"id": "status", "name": "Status", "options": []}]

    def items(self, project_id):
        return copy.deepcopy(self.saved)

    def open_issues(self):
        result = [i for i in self.issues.values() if i["state"] == "open" and "pull_request" not in i]
        self.issues.update(self.live_changes)
        return result

    def issue(self, number):
        return self.issues[number]

    def add(self, project_id, issue_id):
        number = int(issue_id.split("-")[1])
        self.writes.append(("add", number))
        self.saved.append(item(number))
        return "item-%s" % number

    def set_planning(self, project_id, item_id, field_id, option_id):
        assert field_id == "planning", "Status must never be changed"
        self.writes.append(("set", item_id, option_id))
        if not self.ignore_write:
            for row in self.saved:
                if row["id"] == item_id:
                    row["fieldValueByName"] = {"optionId": option_id} if option_id else None


class PlanningTests(unittest.TestCase):
    def test_decision_and_deferral_matrix(self):
        for labels, expected in [([AGREED], "Agreed"), ([DISCUSS], "Needs discussion"),
                                 ([AGREED, DEFERRED], "Deferred"), ([DISCUSS, DEFERRED], "Deferred")]:
            with self.subTest(labels=labels):
                self.assertEqual(expected, planning(labels + ["bug"]))

    def test_invalid_decisions_never_infer_agreement_even_if_deferred(self):
        for labels in ([], [DEFERRED], [AGREED, DISCUSS], [AGREED, DISCUSS, DEFERRED]):
            self.assertIsNone(planning(labels))

    def test_initial_ten_and_idempotence(self):
        choices = {3: [DISCUSS], 4: [AGREED], 5: [AGREED], 6: [AGREED], 7: [DISCUSS],
                   8: [AGREED], 9: [AGREED], 10: [AGREED, DEFERRED],
                   11: [DISCUSS, DEFERRED], 12: [AGREED]}
        api = FakeGitHub([issue(n, labels) for n, labels in choices.items()])
        reconcile(api, lambda _: None)
        values = [i["fieldValueByName"]["optionId"] for i in api.saved]
        self.assertEqual([6, 2, 2], [values.count(v) for v in ("Agreed", "Needs discussion", "Deferred")])
        api.writes.clear()
        reconcile(api, lambda _: None)
        self.assertEqual([], api.writes)

    def test_bad_labels_clear_stale_planning_but_other_items_still_sync(self):
        api = FakeGitHub([issue(3, [AGREED, DISCUSS]), issue(4, [DISCUSS])], [item(3, "Agreed")])
        with self.assertRaisesRegex(RuntimeError, "#3"):
            reconcile(api, lambda _: None)
        self.assertIn(("set", "item-3", None), api.writes)
        self.assertIn(("set", "item-4", "Needs discussion"), api.writes)

    def test_invalid_new_issue_added_unclassified(self):
        api = FakeGitHub([issue(3, [])])
        with self.assertRaises(RuntimeError):
            reconcile(api, lambda _: None)
        self.assertEqual([("add", 3)], api.writes)

    def test_unclassified_valid_issue_recovers(self):
        api = FakeGitHub([issue(3, [AGREED])], [item(3)])
        reconcile(api, lambda _: None)
        self.assertEqual([("set", "item-3", "Agreed")], api.writes)

    def test_archived_drafts_other_repos_and_prs_untouched(self):
        pr = issue(9, [AGREED])
        pr["pull_request"] = {}
        api = FakeGitHub([issue(3, [AGREED]), pr], [item(3, archived=True), item(8, repo="other/repo"),
                            {"id": "draft", "isArchived": False, "content": {}},
                            {"id": "pr", "isArchived": False, "content": {}}])
        reconcile(api, lambda _: None)
        self.assertEqual([], api.writes)

    def test_existing_closed_issue_updated_but_missing_closed_not_added(self):
        api = FakeGitHub([issue(3, [AGREED], "closed"), issue(4, [DISCUSS], "closed")], [item(3, "Deferred")])
        reconcile(api, lambda _: None)
        self.assertEqual([("set", "item-3", "Agreed")], api.writes)

    def test_recheck_labels_after_listing(self):
        api = FakeGitHub([issue(3, [AGREED])], [item(3, "Agreed")])
        api.live_changes[3] = issue(3, [DISCUSS, DEFERRED])
        reconcile(api, lambda _: None)
        self.assertEqual([("set", "item-3", "Deferred")], api.writes)

    def test_closed_during_listing_not_added(self):
        api = FakeGitHub([issue(3, [AGREED])])
        api.live_changes[3] = issue(3, [AGREED], "closed")
        reconcile(api, lambda _: None)
        self.assertEqual([], api.writes)

    def test_schema_change_fails_before_writes(self):
        api = FakeGitHub([issue(3, [AGREED])])
        api.options.append({"name": "New convention", "id": "x"})
        with self.assertRaisesRegex(RuntimeError, "options changed"):
            reconcile(api, lambda _: None)
        self.assertEqual([], api.writes)

    def test_failed_readback_not_reported_as_success(self):
        api = FakeGitHub([issue(3, [AGREED])], [item(3, "Deferred")])
        api.ignore_write = True
        with self.assertRaisesRegex(RuntimeError, "read-back"):
            reconcile(api, lambda _: None)

    def test_graphql_pagination(self):
        calls = []
        def fetch(cursor):
            calls.append(cursor)
            return {"nodes": [cursor or "first"], "pageInfo": {
                "hasNextPage": cursor is None, "endCursor": "next"}}
        self.assertEqual(["first", "next"], list(pages(fetch)))
        self.assertEqual([None, "next"], calls)

    def test_pagination_loop_fails(self):
        with self.assertRaisesRegex(RuntimeError, "did not advance"):
            list(pages(lambda _: {"nodes": [], "pageInfo": {"hasNextPage": True, "endCursor": "same"}}))


if __name__ == "__main__":
    unittest.main()
