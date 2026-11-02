# TraderVI Edge passkey sign-in

Use an existing Microsoft Password Manager passkey in regular Edge and hand the completed session to
TraderVI for verification. No passkeys are copied, security checks bypassed, orders sent or SQL accessed.
[Decision and limits](../../Docs/adr/0123-edge-passkey-session-handoff.md).

## One-time setup (operator installation approval required)

1. Build `TraderVI.WPF/TraderVI.WPF.csproj` with .NET 10. Its output includes `Wealthsimple.EdgeBridge.exe`
   and adjacent DLL/deps/runtimeconfig files; keep them together in a stable directory.
2. Run `Tools/Wealthsimple.EdgeBridge/Install.ps1 -HostExecutable <absolute-path-to-Wealthsimple.EdgeBridge.exe>`.
   It writes `%LOCALAPPDATA%\TraderVI\Wealthsimple\EdgeBridge\host.json` and the default value of
   `HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.tradervi.wealthsimple_login`. No administrator
   access is needed. Rerunning updates the selected output path; it does not copy executables.
3. In `edge://extensions`, enable **Developer mode**, choose **Load unpacked**, select this tool's
   `Extension` directory, and verify ID `ikocfanmafmhmmidhampgihpjnjpiffd`. Pin its toolbar button.
   Review Edge's debugger permission warning: this is powerful browser access, restricted in this
   implementation to the dedicated login tab after you explicitly select **Continue to Wealthsimple**.
   After updating an already-loaded unpacked extension, select **Reload** on its extension card.
4. Launch the approved updated desktop only when desktop replacement/restart is authorized. Extension
   installation does not update an already-running desktop. Do not restart the trading engine.

## Sign in

Close other Wealthsimple Edge tabs first. In TraderVI's connection window choose **Sign in with Edge
passkey**. Edge first opens **Connect TraderVI to Wealthsimple**, a local extension page. Select
**Continue to Wealthsimple**. The extension connects before opening the provider page, including any
automatic passkey prompt. In that prompt, select your saved account under **On this device** and complete
the normal verification. Keep Edge's debugging indicator active. The login tab closes after a supported
response; TraderVI verifies the session before Connected. You do not need to find the toolbar extension.
Remembered sessions use existing Windows encryption.

Cancel in TraderVI or wait five minutes to expire an attempt. Closing the connection window also cancels
it. The extension independently detaches after five minutes or leaving `my.wealthsimple.com`. Cancellation
does not close Edge: close the dedicated tab when abandoning login. Failures require a fresh attempt;
there is no automatic password/challenge/refresh/order replay.

If nothing reaches TraderVI, check the extension is installed in the Edge profile selected by the new
window, registration points to existing adjacent runtime files, the connection page was used, and
the debugging indicator wasn't dismissed. Unsupported DPoP tokens fail. Never share session captures
or private signing keys. Other tabs/background renewal may interfere. Initial native login was operator-confirmed
November 2; subsequent live refresh remains to be qualified. See the
[auth summary and separate engine diagnosis](../../Docs/reviews/wealthsimple-auth-and-engine-diagnosis-20261102.md).

## Offline validation and rollback

Run `node Tools/Wealthsimple.EdgeBridge/Extension/protocol.test.mjs`,
`node Tools/Wealthsimple.EdgeBridge/Extension/background.test.mjs` and
`node Tools/Wealthsimple.EdgeBridge/Extension/start.test.mjs` (synthetic data/mocked browser APIs),
focused Core tests including `WealthsimpleEdgeHandoffTests`, and a WPF build. Do not launch trading hosts.
`Test-NativeHost.ps1 -HostExecutable <built-helper-exe>` also exercises the real helper process using
synthetic messages, a private local pipe and a verification receipt; it checks clean protocol stdout.

To uninstall, remove the extension in Edge, then remove only the per-user registry key above and
`EdgeBridge\host.json`. Existing passkeys, protected TraderVI sessions, journals and account state remain.
No rollback script runs automatically.
