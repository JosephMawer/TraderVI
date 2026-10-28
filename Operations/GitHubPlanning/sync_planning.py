"""One-way, repository-scoped label -> Project Planning reconciliation.

Only project membership and Planning values are written. No repository writes,
Status changes, credentials on disk, third-party dependencies or trading calls.
"""
import json
import os
import sys
import urllib.error
import urllib.request

REPO = "JosephMawer/TraderVI"
OWNER = "JosephMawer"
PROJECT_NUMBER = 1
PROJECT_TITLE = "TraderVI Work"
AGREED = "decision: agreed"
DISCUSS = "decision: needs discussion"
DEFERRED = "planning: deferred"
OPTIONS = {"Agreed", "Needs discussion", "Deferred"}


def planning(labels):
    labels = set(labels)
    if (AGREED in labels) == (DISCUSS in labels):
        return None  # Missing or contradictory decision; never infer agreement.
    if DEFERRED in labels:
        return "Deferred"
    return "Agreed" if AGREED in labels else "Needs discussion"


def pages(fetch_page):
    cursor = None
    seen = set()
    for _ in range(100):
        page = fetch_page(cursor)
        yield from page["nodes"]
        if not page["pageInfo"]["hasNextPage"]:
            return
        cursor = page["pageInfo"]["endCursor"]
        if not cursor or cursor in seen:
            raise RuntimeError("Pagination did not advance; no further processing.")
        seen.add(cursor)
    raise RuntimeError("Pagination safety bound exceeded.")


class GitHub:
    def __init__(self, project_token, repo_token):
        self.project_token = project_token
        self.repo_token = repo_token

    def request(self, url, token, body=None):
        payload = None if body is None else json.dumps(body).encode("utf-8")
        req = urllib.request.Request(url, data=payload, headers={
            "Authorization": "Bearer " + token,
            "Accept": "application/vnd.github+json",
            "Content-Type": "application/json",
            "User-Agent": "TraderVI-planning-sync",
        })
        try:
            with urllib.request.urlopen(req, timeout=30) as response:
                return json.load(response)
        except urllib.error.HTTPError as exc:
            # Do not dump request headers, credentials or arbitrary API bodies.
            raise RuntimeError("GitHub HTTP %s; check token expiry, scopes and access." % exc.code) from None

    def graphql(self, query, **variables):
        result = self.request("https://api.github.com/graphql", self.project_token,
                              {"query": query, "variables": variables})
        if result.get("errors"):
            raise RuntimeError("GitHub GraphQL rejected the request; check access and Project schema.")
        return result["data"]

    def rest(self, path):
        return self.request("https://api.github.com/repos/" + REPO + path, self.repo_token)

    def open_issues(self):
        for page in range(1, 101):
            rows = self.rest("/issues?state=open&per_page=100&page=" + str(page))
            for row in rows:
                if "pull_request" not in row:
                    yield row
            if len(rows) < 100:
                return
        raise RuntimeError("Issue pagination safety bound exceeded.")

    def issue(self, number):
        return self.rest("/issues/" + str(number))

    def project(self):
        result = self.graphql("""query($owner:String!, $number:Int!) {
          user(login:$owner) { projectV2(number:$number) { id title closed } }
        }""", owner=OWNER, number=PROJECT_NUMBER)
        project = (result.get("user") or {}).get("projectV2")
        if not project or project["title"] != PROJECT_TITLE or project["closed"]:
            raise RuntimeError("Expected open TraderVI Work project was not found; no writes.")
        return project

    def fields(self, project_id):
        return list(pages(lambda cursor: self.graphql("""
          query($id:ID!, $cursor:String) { node(id:$id) { ... on ProjectV2 {
            fields(first:100, after:$cursor) {
              nodes { ... on ProjectV2SingleSelectField { id name options { id name } } }
              pageInfo { hasNextPage endCursor }
            }
          } } }
        """, id=project_id, cursor=cursor)["node"]["fields"]))

    def items(self, project_id):
        return list(pages(lambda cursor: self.graphql("""
          query($id:ID!, $cursor:String) { node(id:$id) { ... on ProjectV2 {
            items(first:100, after:$cursor) {
              nodes { id isArchived
                content { ... on Issue { number repository { nameWithOwner } } }
                fieldValueByName(name:"Planning") {
                  ... on ProjectV2ItemFieldSingleSelectValue { optionId }
                }
              }
              pageInfo { hasNextPage endCursor }
            }
          } } }
        """, id=project_id, cursor=cursor)["node"]["items"]))

    def add(self, project_id, issue_id):
        result = self.graphql("""mutation($project:ID!, $content:ID!) {
          addProjectV2ItemById(input:{projectId:$project, contentId:$content}) {
            item { id }
          }
        }""", project=project_id, content=issue_id)
        return result["addProjectV2ItemById"]["item"]["id"]

    def set_planning(self, project_id, item_id, field_id, option_id):
        if option_id is None:
            self.graphql("""mutation($project:ID!, $item:ID!, $field:ID!) {
              clearProjectV2ItemFieldValue(input:{projectId:$project, itemId:$item, fieldId:$field}) {
                projectV2Item { id }
              }
            }""", project=project_id, item=item_id, field=field_id)
        else:
            self.graphql("""mutation($project:ID!, $item:ID!, $field:ID!, $option:String!) {
              updateProjectV2ItemFieldValue(input:{projectId:$project, itemId:$item,
                fieldId:$field, value:{singleSelectOptionId:$option}}) {
                projectV2Item { id }
              }
            }""", project=project_id, item=item_id, field=field_id, option=option_id)


def reconcile(api, log=print):
    project = api.project()
    fields = [f for f in api.fields(project["id"]) if f.get("name") == "Planning"]
    if len(fields) != 1:
        raise RuntimeError("Expected one single-select Planning field; no writes.")
    field = fields[0]
    options = {o["name"]: o["id"] for o in field["options"]}
    if set(options) != OPTIONS or len(field["options"]) != 3:
        raise RuntimeError("Planning options changed; review conventions before any writes.")
    items = {}
    archived = set()
    for item in api.items(project["id"]):
        content = item.get("content") or {}
        if (content.get("repository") or {}).get("nameWithOwner") != REPO:
            continue  # Other repositories, drafts, PRs and deleted content are untouched.
        number = content["number"]
        if item["isArchived"]:
            archived.add(number)
        else:
            items[number] = item
    numbers = set(items) | {i["number"] for i in api.open_issues()}
    invalid = []
    checked = changed = 0
    expected_values = {}
    for number in sorted(numbers - archived):
        issue = api.issue(number)  # Re-read live labels; queued event payloads can be stale.
        item = items.get(number)
        if "pull_request" in issue or (item is None and issue["state"] != "open"):
            continue
        expected = planning(label["name"] for label in issue["labels"])
        if expected is None:
            invalid.append(number)
        if item is None:
            item = {"id": api.add(project["id"], issue["node_id"]), "fieldValueByName": None}
        wanted = options.get(expected)
        expected_values[item["id"]] = wanted
        current = (item.get("fieldValueByName") or {}).get("optionId")
        if current != wanted:
            api.set_planning(project["id"], item["id"], field["id"], wanted)
            changed += 1
        checked += 1
        log("#%s: %s" % (number, expected or "Needs label review (Planning cleared)"))
    observed = {i["id"]: (i.get("fieldValueByName") or {}).get("optionId")
                for i in api.items(project["id"])}
    if any(key not in observed or observed[key] != value for key, value in expected_values.items()):
        raise RuntimeError("Project read-back did not match requested Planning values; rerun reconciliation.")
    log("Verified %s issue(s); changed %s Planning value(s)." % (checked, changed))
    if invalid:
        raise RuntimeError("Exactly one decision label is required on: " + ", ".join("#" + str(n) for n in invalid))


def main():
    lines = ["# TraderVI Planning synchronization"]

    def log(message):
        print(message)
        lines.append(message)

    try:
        project_token = os.environ.get("TRADERVI_PROJECT_TOKEN", "")
        repo_token = os.environ.get("GH_REPO_TOKEN", "")
        if not project_token or not repo_token:
            raise RuntimeError("Set repository Actions secret TRADERVI_PROJECT_TOKEN (classic PAT, project scope). GH_REPO_TOKEN is supplied by Actions.")
        reconcile(GitHub(project_token, repo_token), log)
        return 0
    except Exception as exc:
        # Only our controlled RuntimeError messages are printed; transport exceptions
        # can contain URL details. Never render raw issue titles, bodies or labels.
        message = str(exc) if isinstance(exc, RuntimeError) else type(exc).__name__ + "; synchronization failed."
        log("ERROR: " + message)
        return 1
    finally:
        if os.environ.get("GITHUB_STEP_SUMMARY"):
            with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as stream:
                stream.write("\n\n".join(lines) + "\n")


if __name__ == "__main__":
    sys.exit(main())
