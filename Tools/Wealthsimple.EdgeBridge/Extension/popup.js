document.getElementById("connect").addEventListener("click", async () => {
  const status = document.getElementById("status");
  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    const result = await chrome.runtime.sendMessage({ action: "connect", tabId: tab.id, url: tab.url });
    status.textContent = result.ok ? "Ready. Sign in using your saved passkey. This login tab will close after handoff." :
      "Could not connect. Open a new Edge sign-in from TraderVI and try again.";
  } catch { status.textContent = "Could not connect. Check TraderVI and try again."; }
});
