import { pairingFromUrl, requestMetadata, tokenFields, tokenUrl } from "./protocol.mjs";
const host = "com.tradervi.wealthsimple_login";
const logins = new Map(); // In memory only. No cookies, passwords, passkeys or persistent session storage.
async function stop(tabId, failed = false) {
  logins.delete(tabId);
  await chrome.alarms.clear(`login:${tabId}`);
  try { await chrome.debugger.detach({ tabId }); } catch { }
  try {
    await chrome.action.setBadgeText({ tabId, text: failed ? "!" : "" });
    await chrome.action.setTitle({ tabId, title: failed ? "Sign-in handoff failed. Retry from TraderVI." : "TraderVI sign-in" });
  } catch { }
}
chrome.runtime.onMessage.addListener((message, sender, reply) => {
  if (sender.id !== chrome.runtime.id || message.action !== "connect") return;
  (async () => {
    const tab = await chrome.tabs.get(message.tabId);
    const pairing = pairingFromUrl(tab.url);
    if (!pairing || logins.has(tab.id)) throw new Error("Unpaired tab");
    await chrome.debugger.attach({ tabId: tab.id }, "1.3");
    logins.set(tab.id, { pairing, requests: new Map(), finishing: false });
    await chrome.alarms.create(`login:${tab.id}`, { delayInMinutes: 5 });
    await chrome.debugger.sendCommand({ tabId: tab.id }, "Network.enable");
    await chrome.debugger.sendCommand({ tabId: tab.id }, "Page.enable");
    await chrome.action.setBadgeText({ tabId: tab.id, text: "ON" });
    // Remove the pairing fragment before the operator submits the provider's login form.
    await chrome.tabs.update(tab.id, { url: "https://my.wealthsimple.com/app/login" });
    return { ok: true };
  })().then(reply).catch(async () => { await stop(message.tabId, true); reply({ ok: false }); });
  return true;
});
chrome.debugger.onDetach.addListener(source => { logins.delete(source.tabId); chrome.alarms.clear(`login:${source.tabId}`); });
chrome.tabs.onRemoved.addListener(tabId => { logins.delete(tabId); chrome.alarms.clear(`login:${tabId}`); });
chrome.tabs.onUpdated.addListener((tabId, change) => {
  if (logins.has(tabId) && change.url && new URL(change.url).origin !== "https://my.wealthsimple.com")
    void stop(tabId, true);
});
chrome.alarms.onAlarm.addListener(alarm => {
  if (alarm.name.startsWith("login:")) void stop(Number(alarm.name.slice(6)), true);
});
chrome.debugger.onEvent.addListener((source, method, params) => {
  const login = logins.get(source.tabId);
  if (!login || login.finishing) return;
  void observe(source, method, params, login).catch(() => stop(source.tabId, true));
});
async function observe(source, method, params, login) {
  if (method === "Page.frameNavigated" && !params.frame.parentId &&
      new URL(params.frame.url).origin !== "https://my.wealthsimple.com") {
    await stop(source.tabId, true);
    return;
  }
  if (method === "Network.requestWillBeSent") {
    // Retain only public metadata from this exact endpoint. Never request omitted post data.
    const metadata = requestMetadata(params.request);
    if (metadata) {
      if (login.requests.size >= 32) throw new Error("Too many login exchanges");
      login.requests.set(params.requestId, metadata);
    } else login.requests.delete(params.requestId);
  }
  const entry = login.requests.get(params.requestId);
  if (!entry) return;
  if (method === "Network.responseReceived") {
    if (params.response.url !== tokenUrl || params.response.status !== 200) login.requests.delete(params.requestId);
    else entry.success = true;
  }
  if (method === "Network.loadingFailed") login.requests.delete(params.requestId);
  if (method !== "Network.loadingFinished" || !entry.success) return;
  login.finishing = true;
  const result = await chrome.debugger.sendCommand(source, "Network.getResponseBody", { requestId: params.requestId });
  const response = tokenFields(result.base64Encoded ? atob(result.body) : result.body);
  const payload = { pairing: login.pairing, url: tokenUrl, method: "POST", status: 200,
    request: entry.request, deviceId: entry.deviceId, sessionId: entry.sessionId, response };
  // End the browser's ownership before the native client can verify or refresh the session.
  await stop(source.tabId);
  await chrome.tabs.remove(source.tabId);
  const receipt = await chrome.runtime.sendNativeMessage(host, payload);
  if (receipt?.connected !== true) throw new Error("Handoff failed");
}
