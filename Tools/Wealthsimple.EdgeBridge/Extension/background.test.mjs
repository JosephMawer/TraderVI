import assert from "assert";
import { readFileSync } from "fs";
import { pairingFromUrl, pairingFromStartUrl, requestMetadata, tokenFields, tokenUrl } from "./protocol.mjs";

const pairing = "a".repeat(64);
const events = {};
const calls = [];
const startUrl = "chrome-extension://synthetic-extension/start.html";
const event = key => ({ addListener: callback => { events[key] = callback; } });
const chrome = {
  runtime: { id: "synthetic-extension", getURL: path => `chrome-extension://synthetic-extension/${path}`, onMessage: event("message"),
    sendNativeMessage: async (host, payload) => { calls.push(["native", host, payload]); return { connected: true }; } },
  debugger: { onDetach: event("detach"), onEvent: event("debug"),
    attach: async source => calls.push(["attach", source.tabId]),
    detach: async source => calls.push(["detach", source.tabId]),
    sendCommand: async (source, command) => {
      calls.push([command, source.tabId]);
      if (command === "Network.getResponseBody") return { body: JSON.stringify({ token_type: "Bearer", access_token: "synthetic-token", account: "private-account" }) };
    } },
  tabs: { onRemoved: event("removed"), onUpdated: event("updated"),
    get: async id => ({ id, url: id >= 6 ? `${startUrl}#tradervi-login=${pairing}` : `https://my.wealthsimple.com/app/login#tradervi-login=${pairing}` }),
    update: async (id, update) => calls.push(["update", id, update]), remove: async id => calls.push(["close", id]) },
  alarms: { onAlarm: event("alarm"), create: async () => {}, clear: async () => {} },
  action: { setBadgeText: async () => {}, setTitle: async () => {} }
};
const code = readFileSync(new URL("./background.mjs", import.meta.url), "utf8").replace(/^import .*;\r?\n/, "");
new Function("chrome", "pairingFromUrl", "pairingFromStartUrl", "requestMetadata", "tokenFields", "tokenUrl", code)(
  chrome, pairingFromUrl, pairingFromStartUrl, requestMetadata, tokenFields, tokenUrl);
const tick = () => new Promise(resolve => setImmediate(resolve));
const connect = id => new Promise(resolve => events.message({ action: "connect", tabId: id }, { id: chrome.runtime.id }, resolve));
const request = (id, tabId = 1, overrides = {}) => events.debug({ tabId }, "Network.requestWillBeSent", {
  requestId: id, request: { url: tokenUrl, method: "POST", postData: JSON.stringify({ grant_type: "webauthn", client_id: "synthetic-client", assertion: "private-assertion" }), ...overrides }
});
const response = (id, status = 200, tabId = 1) => events.debug({ tabId }, "Network.responseReceived", {
  requestId: id, response: { url: tokenUrl, status }
});
const finished = (id, tabId = 1) => events.debug({ tabId }, "Network.loadingFinished", { requestId: id });

assert.deepEqual(await connect(1), { ok: true });
request("unpaired", 2); response("unpaired", 200, 2); finished("unpaired", 2);
request("failed"); response("failed", 401); finished("failed");
request("other", 1, { url: "https://attacker.invalid/token" }); response("other"); finished("other");
await tick();
assert.equal(calls.filter(c => c[0] === "Network.getResponseBody").length, 0);
request("success"); response("success"); finished("success");
await tick(); await tick();
const native = calls.find(c => c[0] === "native");
assert.ok(native);
assert.equal(native[2].pairing, pairing);
assert.deepEqual(native[2].request, { grant_type: "webauthn", client_id: "synthetic-client" });
assert.equal(native[2].response.account, undefined);
assert.ok(calls.findIndex(c => c[0] === "detach") < calls.findIndex(c => c[0] === "close"));
assert.ok(calls.findIndex(c => c[0] === "close") < calls.findIndex(c => c[0] === "native"));
finished("success"); await tick();
assert.equal(calls.filter(c => c[0] === "native").length, 1);

await connect(3);
events.debug({ tabId: 3 }, "Page.frameNavigated", { frame: { url: "https://attacker.invalid/" } });
await tick();
assert.ok(calls.some(c => c[0] === "detach" && c[1] === 3));
await connect(4);
events.alarm({ name: "login:4" }); await tick();
assert.ok(calls.some(c => c[0] === "detach" && c[1] === 4));
await connect(5);
events.detach({ tabId: 5 });
request("dismissed", 5); response("dismissed", 200, 5); finished("dismissed", 5); await tick();
assert.equal(calls.filter(c => c[0] === "native").length, 1);
const startReply = await new Promise(resolve => events.message({ action: "start", tabId: 6 },
  { id: chrome.runtime.id, url: `${startUrl}#tradervi-login=${pairing}` }, resolve));
assert.deepEqual(startReply, { ok: true });
const indexFor = command => calls.findIndex(c => c[0] === command && c[1] === 6);
assert.ok(indexFor("attach") < indexFor("Network.enable"));
assert.ok(indexFor("Network.enable") < indexFor("update"));
assert.ok(indexFor("Page.enable") < indexFor("update"));
const rejected = await new Promise(resolve => events.message({ action: "start", tabId: 7 },
  { id: chrome.runtime.id, url: "https://attacker.invalid/" }, resolve));
assert.deepEqual(rejected, { ok: false });
assert.equal(calls.some(c => c[0] === "attach" && c[1] === 7), false);
console.log("Edge extension event checks passed");
