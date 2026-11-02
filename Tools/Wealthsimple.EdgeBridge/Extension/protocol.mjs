export const tokenUrl = "https://api.production.wealthsimple.com/v1/oauth/v2/token";
export function pairingFromStartUrl(value, startUrl) {
  if (typeof value !== "string" || !value.startsWith(startUrl + "#")) return null;
  return /^#tradervi-login=([a-f0-9]{64})$/.exec(value.slice(startUrl.length))?.[1] ?? null;
}
export function pairingFromUrl(value) {
  const url = new URL(value);
  if (url.origin !== "https://my.wealthsimple.com" || url.pathname !== "/app/login" || url.search) return null;
  const match = /^#tradervi-login=([a-f0-9]{64})$/.exec(url.hash);
  return match?.[1] ?? null;
}
export function requestMetadata(request) {
  if (request.url !== tokenUrl || request.method !== "POST" || typeof request.postData !== "string") return null;
  const body = JSON.parse(request.postData);
  if (!["password", "refresh_token", "webauthn"].includes(body.grant_type) || typeof body.client_id !== "string") return null;
  const header = name => Object.entries(request.headers ?? {}).find(([key]) => key.toLowerCase() === name)?.[1] ?? null;
  return { request: { grant_type: body.grant_type, client_id: body.client_id },
    deviceId: header("x-ws-device-id"), sessionId: header("x-ws-session-id") };
}
export function tokenFields(text) {
  const body = JSON.parse(text);
  if (typeof body.token_type !== "string" || body.token_type.toLowerCase() !== "bearer")
    throw new Error("Unsupported session type");
  return Object.fromEntries(["access_token", "refresh_token", "token_type", "created_at", "expires_in"]
    .filter(key => body[key] !== undefined).map(key => [key, body[key]]));
}
