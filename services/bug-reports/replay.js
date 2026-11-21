import {validCombatReplay} from "./validation.js";

export async function decodeReplay(replay) {
  if (!validCombatReplay(replay) || !replay.available) throw new Error("replay_unavailable");
  const raw = atob(replay.data);
  if (raw.length > 265000) throw new Error("invalid_replay");
  const bytes = new Uint8Array(raw.length);
  for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
  const reader = new Blob([bytes]).stream().pipeThrough(new DecompressionStream("gzip")).getReader();
  const chunks = [];
  let length = 0;
  try {
    while (true) {
      const {done, value} = await reader.read();
      if (done) break;
      length += value.length;
      if (length > 2000000) {
        await reader.cancel();
        throw new Error("invalid_replay");
      }
      chunks.push(value);
    }
  } finally {
    reader.releaseLock();
  }
  if (length !== replay.plain_bytes) throw new Error("invalid_replay");
  const plain = new Uint8Array(length);
  let offset = 0;
  for (const chunk of chunks) {
    plain.set(chunk, offset);
    offset += chunk.length;
  }
  const hash = Array.from(new Uint8Array(await crypto.subtle.digest("SHA-256", plain)), b => b.toString(16).padStart(2, "0")).join("");
  if (hash !== replay.sha256.toLowerCase()) throw new Error("invalid_replay");
  return plain;
}

export function replayExtension(plainBytes) {
  try {
    const decoded = new TextDecoder("utf-8", {fatal: true}).decode(plainBytes).trim();
    if ((decoded.startsWith("{") && decoded.endsWith("}")) || (decoded.startsWith("[") && decoded.endsWith("]"))) {
      JSON.parse(decoded);
      return ".json";
    }
  } catch {}
  return ".mcr";
}
