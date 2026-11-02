import assert from "assert";
import { readFileSync } from "fs";
import { pairingFromStartUrl } from "./protocol.mjs";
const startUrl = "chrome-extension://synthetic-extension/start.html";
const script = readFileSync(new URL("./start.mjs", import.meta.url), "utf8").replace(/^import .*;\r?\n/, "");
function fixture(href, ok = true) {
  const calls = [];
  const button = { disabled: false, addEventListener: (event, callback) => { button.click = callback; } };
  const status = { textContent: "" };
  const document = { getElementById: id => id === "continue" ? button : status };
  const chrome = { runtime: { getURL: () => startUrl, sendMessage: async message => { calls.push(message); return { ok }; } },
    tabs: { getCurrent: async () => ({ id: 42 }) } };
  new Function("document", "chrome", "location", "pairingFromStartUrl", script)(document, chrome, { href }, pairingFromStartUrl);
  return { calls, button, status };
}
const invalid = fixture(startUrl);
assert.equal(invalid.button.disabled, true);
assert.ok(invalid.status.textContent.includes("fresh sign-in"));
const ready = fixture(`${startUrl}#tradervi-login=${"a".repeat(64)}`);
await ready.button.click();
assert.deepEqual(ready.calls, [{ action: "start", tabId: 42 }]);
assert.equal(ready.button.disabled, true);
const failed = fixture(`${startUrl}#tradervi-login=${"a".repeat(64)}`, false);
await failed.button.click();
assert.equal(failed.button.disabled, false);
assert.ok(failed.status.textContent.includes("Cancel Edge sign-in"));
console.log("Edge connection-page checks passed");
