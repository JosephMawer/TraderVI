import { pairingFromStartUrl } from "./protocol.mjs";
const button = document.getElementById("continue");
const status = document.getElementById("status");
if (!pairingFromStartUrl(location.href, chrome.runtime.getURL("start.html"))) {
  button.disabled = true;
  status.textContent = "Start a fresh sign-in using Sign in with Edge passkey in TraderVI.";
}
button.addEventListener("click", async () => {
  button.disabled = true;
  status.textContent = "Connecting this tab before opening Wealthsimple…";
  try {
    const tab = await chrome.tabs.getCurrent();
    const result = await chrome.runtime.sendMessage({ action: "start", tabId: tab.id });
    if (!result?.ok) throw new Error("Connection failed");
  } catch {
    status.textContent = "Could not connect. Cancel Edge sign-in in TraderVI and start again. Check that the extension is enabled.";
    button.disabled = false;
  }
});
