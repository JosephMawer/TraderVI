import assert from "assert";
import { pairingFromUrl, pairingFromStartUrl, requestMetadata, tokenFields, tokenUrl } from "./protocol.mjs";
import { createHash } from "crypto";
import { readFileSync } from "fs";

const pairing = "a".repeat(64);
const startUrl = "chrome-extension://ikocfanmafmhmmidhampgihpjnjpiffd/start.html";
assert.equal(pairingFromStartUrl(`${startUrl}#tradervi-login=${pairing}`, startUrl), pairing);
for (const url of [`${startUrl}?extra=1#tradervi-login=${pairing}`, `${startUrl}.evil#tradervi-login=${pairing}`,
  `https://attacker.invalid/start.html#tradervi-login=${pairing}`, `${startUrl}#tradervi-login=../pipe`])
  assert.equal(pairingFromStartUrl(url, startUrl), null);
assert.equal(pairingFromUrl(`https://my.wealthsimple.com/app/login#tradervi-login=${pairing}`), pairing);
for (const url of ["https://attacker.invalid/app/login", "https://my.wealthsimple.com.evil.invalid/app/login",
  `https://my.wealthsimple.com/app/login?extra=1#tradervi-login=${pairing}`, "https://my.wealthsimple.com/app/login#tradervi-login=../pipe"])
  assert.equal(pairingFromUrl(url), null);
const request = { url: tokenUrl, method: "POST", headers: { "X-WS-Device-ID": "synthetic-device" },
  postData: JSON.stringify({ grant_type: "webauthn", client_id: "synthetic-client", password: "synthetic-secret", assertion: "synthetic-private" }) };
assert.deepEqual(requestMetadata(request), { request: { grant_type: "webauthn", client_id: "synthetic-client" },
  deviceId: "synthetic-device", sessionId: null });
for (const change of [{ url: tokenUrl + "?extra=1" }, { url: "https://attacker.invalid/token" }, { method: "GET" },
  { postData: JSON.stringify({ grant_type: "client_credentials", client_id: "synthetic-client" }) }])
  assert.equal(requestMetadata({ ...request, ...change }), null);
assert.deepEqual(tokenFields(JSON.stringify({ token_type: "Bearer", access_token: "synthetic-access", account: "private-account" })),
  { access_token: "synthetic-access", token_type: "Bearer" });
assert.throws(() => tokenFields('{"token_type":"DPoP"}'));
const manifest = JSON.parse(readFileSync(new URL("./manifest.json", import.meta.url)));
const id = [...createHash("sha256").update(Buffer.from(manifest.key, "base64")).digest().subarray(0, 16)]
  .map(b => String.fromCharCode(97 + (b >> 4), 97 + (b & 15))).join("");
assert.equal(id, "ikocfanmafmhmmidhampgihpjnjpiffd");
assert.equal(manifest.host_permissions, undefined);
assert.equal(manifest.content_scripts, undefined);
console.log("Edge bridge protocol checks passed");
