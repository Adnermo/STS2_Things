import {validScreenshot} from "./validation.js";

export async function decodeScreenshot(image) {
  if (!validScreenshot(image) || !image.available) throw new Error("screenshot_unavailable");
  const raw = atob(image.data);
  if (raw.length !== image.bytes || raw.length > 250000) throw new Error("invalid_screenshot");
  const bytes = new Uint8Array(raw.length);
  for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
  if (bytes[0] !== 0xff || bytes[1] !== 0xd8 || bytes[2] !== 0xff) throw new Error("invalid_screenshot");
  const hash = Array.from(new Uint8Array(await crypto.subtle.digest("SHA-256", bytes)), b => b.toString(16).padStart(2, "0")).join("");
  if (hash !== image.sha256.toLowerCase()) throw new Error("invalid_screenshot");
  return bytes;
}
