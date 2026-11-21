// Deterministic report fingerprinting, clustering, and external mod attribution.

const K = new Uint32Array([
  0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
  0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
  0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
  0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
  0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
  0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
  0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
  0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
]);

export function sha256Hex(input) {
  const bytes = new TextEncoder().encode(String(input));
  const bitLen = bytes.length * 8;
  const totalLen = (((bytes.length + 9 + 63) >> 6) << 6);
  const buf = new Uint8Array(totalLen);
  buf.set(bytes);
  buf[bytes.length] = 0x80;
  const view = new DataView(buf.buffer);
  view.setUint32(totalLen - 8, Math.floor(bitLen / 0x100000000), false);
  view.setUint32(totalLen - 4, bitLen >>> 0, false);

  let h0 = 0x6a09e667, h1 = 0xbb67ae85, h2 = 0x3c6ef372, h3 = 0xa54ff53a;
  let h4 = 0x510e527f, h5 = 0x9b05688c, h6 = 0x1f83d9ab, h7 = 0x5be0cd19;
  const w = new Uint32Array(64);

  for (let offset = 0; offset < totalLen; offset += 64) {
    for (let i = 0; i < 16; i++) w[i] = view.getUint32(offset + i * 4, false);
    for (let i = 16; i < 64; i++) {
      const s0 = ((w[i - 15] >>> 7) | (w[i - 15] << 25)) ^ ((w[i - 15] >>> 18) | (w[i - 15] << 14)) ^ (w[i - 15] >>> 3);
      const s1 = ((w[i - 2] >>> 17) | (w[i - 2] << 15)) ^ ((w[i - 2] >>> 19) | (w[i - 2] << 13)) ^ (w[i - 2] >>> 10);
      w[i] = (w[i - 16] + s0 + w[i - 7] + s1) >>> 0;
    }
    let a = h0, b = h1, c = h2, d = h3, e = h4, f = h5, g = h6, h = h7;
    for (let i = 0; i < 64; i++) {
      const S1 = ((e >>> 6) | (e << 26)) ^ ((e >>> 11) | (e << 21)) ^ ((e >>> 25) | (e << 7));
      const ch = (e & f) ^ (~e & g);
      const temp1 = (h + S1 + ch + K[i] + w[i]) >>> 0;
      const S0 = ((a >>> 2) | (a << 30)) ^ ((a >>> 13) | (a << 19)) ^ ((a >>> 22) | (a << 10));
      const maj = (a & b) ^ (a & c) ^ (b & c);
      const temp2 = (S0 + maj) >>> 0;
      h = g; g = f; f = e; e = (d + temp1) >>> 0;
      d = c; c = b; b = a; a = (temp1 + temp2) >>> 0;
    }
    h0 = (h0 + a) >>> 0; h1 = (h1 + b) >>> 0; h2 = (h2 + c) >>> 0; h3 = (h3 + d) >>> 0;
    h4 = (h4 + e) >>> 0; h5 = (h5 + f) >>> 0; h6 = (h6 + g) >>> 0; h7 = (h7 + h) >>> 0;
  }
  return [h0, h1, h2, h3, h4, h5, h6, h7].map(n => n.toString(16).padStart(8, "0")).join("");
}

const arr = v => Array.isArray(v) ? v : [];
const normToken = s => String(s || "").toLowerCase().replace(/[^a-z0-9\u4e00-\u9fa5]/g, "");

const VANILLA_OR_ENGINE_ROOTS = new Set([
  "system", "microsoft", "godot", "godotsharp", "megacrit", "sts2", "sts2_things",
  "harmonylib", "monomod", "newtonsoft", "netstandard", "mscorlib", "lambda_method",
]);

const KNOWN_EXTERNAL_CANONICAL = new Map([
  ["cardarteditor", "CardArtEditor"],
  ["bettercallspire", "BetterCallSpire"],
  ["momolib", "MomoLib"],
  ["baselib", "BaseLib"],
  ["ritsulib", "RitsuLib"],
]);

const PORTRAIT_HINT = /portrait|card[_.-]?art|art[_.-]?pack|立绘|卡图|美化/i;

function sanitizeKey(s, fallback = "unknown") {
  const cleaned = String(s || "")
    .trim()
    .replace(/[^A-Za-z0-9_.:-]+/g, "_")
    .replace(/^_+|_+$/g, "");
  return (cleaned || fallback).slice(0, 120);
}

function shortExceptionName(rawType, message = "") {
  const candidate = String(rawType || "").trim();
  if (candidate && candidate !== "runtime_error" && candidate !== "error") {
    const base = candidate.split(/[:\s]/)[0].replace(/`\d+$/, "");
    const parts = base.split(".");
    return parts[parts.length - 1] || base;
  }
  const match = String(message || "").match(/\b([A-Za-z0-9_]+(?:Exception|Error))\b/);
  if (match) return match[1];
  return candidate || "RuntimeError";
}

export function parseStackFrame(rawLine) {
  if (typeof rawLine !== "string") return null;
  let line = rawLine.trim();
  if (!line || line === "--- End of stack trace from previous location ---" || line.startsWith("---")) return null;
  line = line.replace(/^(?:at|in|->)\s+/i, "").trim();
  // Reject plain prose messages that aren't stack frames.
  if (/\s/.test(line) && !/^(?:DMD|Trampoline)<|\(/.test(line) && line.includes(": ")) return null;

  const hadHarmonyMarker = /_Patch\d+\b|^DMD<|^Trampoline<|HarmonyLib\.|MonoMod\./i.test(line);

  // Strip trailing source file / IL offset markers.
  line = line.replace(/\s+\[0x[0-9a-f]+\]\s+in\s+.*$/i, "");
  line = line.replace(/\s+in\s+(?:[A-Za-z]:[\\/]|\/|<).*$/i, "");

  // Unwrap Harmony DMD<ReturnType Namespace.Type::Method(Args)> or DMD<Namespace.Type::Method>.
  const dmdMatch = line.match(/^(?:DMD|Trampoline)<([^>]+)>/i);
  if (dmdMatch) {
    let inner = dmdMatch[1].trim();
    const parenIdx = inner.indexOf("(");
    if (parenIdx !== -1) inner = inner.slice(0, parenIdx);
    const spaceIdx = inner.lastIndexOf(" ");
    if (spaceIdx !== -1) inner = inner.slice(spaceIdx + 1);
    line = inner.replace(/::/g, ".");
  } else {
    const parenIdx = line.indexOf("(");
    if (parenIdx !== -1) line = line.slice(0, parenIdx);
    line = line.replace(/::/g, ".").trim();
  }

  // Normalize C# async state machine & compiler-generated closures.
  line = line.replace(/[.+]<([A-Za-z0-9_]+)>d__\d+\.MoveNext$/i, ".$1");
  line = line.replace(/[.+]<>c(?:__DisplayClass\d+_\d+)?\.<([A-Za-z0-9_]+)>b__\d+$/i, ".$1");
  line = line.replace(/_Patch\d+$/i, "");
  line = line.replace(/`\d+/g, "");

  if (!line || !/^[A-Za-z0-9_<>.+]+$/.test(line)) return null;
  return {raw: rawLine.trim(), methodPath: line, hadHarmonyMarker};
}

function extractFramesFromStack(stackInput, topFrame = "") {
  const lines = [];
  if (typeof topFrame === "string" && topFrame.trim()) lines.push(topFrame);
  if (Array.isArray(stackInput)) {
    for (const item of stackInput) if (typeof item === "string") lines.push(...item.split(/\r?\n/));
  } else if (typeof stackInput === "string") {
    lines.push(...stackInput.split(/\r?\n/));
  }
  const seen = new Set();
  const parsed = [];
  for (const line of lines) {
    const frame = parseStackFrame(line);
    if (frame && !seen.has(frame.methodPath + "|" + frame.hadHarmonyMarker)) {
      seen.add(frame.methodPath + "|" + frame.hadHarmonyMarker);
      parsed.push(frame);
    }
  }
  return parsed;
}

function isWrapperFrame(methodPath) {
  return (
    /^Godot\.GD\./i.test(methodPath)
    || /^MegaCrit\.Sts2\.Core\.Logging\./i.test(methodPath)
    || /^MegaCrit\.Sts2\.Core\.Helpers\.TaskHelper\.LogTaskExceptions\b/i.test(methodPath)
    || /^System\.Environment\.(?:get_StackTrace|StackTrace)\b/i.test(methodPath)
    || /^System\.Diagnostics\.StackTrace\b/i.test(methodPath)
    || /^STS2_Things\.Diagnostics\./i.test(methodPath)
    || /^System\.Runtime\.CompilerServices\.(?:TaskAwaiter|AsyncTaskMethodBuilder|AsyncMethodBuilderCore|AsyncValueTaskMethodBuilder)\b/i.test(methodPath)
    || /^System\.Runtime\.ExceptionServices\.ExceptionDispatchInfo\b/i.test(methodPath)
    || /^System\.ThrowHelper\./i.test(methodPath)
    || /^(?:HarmonyLib|MonoMod)\./i.test(methodPath)
  );
}

function isEngineOrBclFrame(methodPath) {
  return /^(?:System|Microsoft|Godot)(?:\.|$)/i.test(methodPath);
}

function shortMethodLabel(methodPath) {
  const parts = String(methodPath || "").split(".").filter(Boolean);
  if (parts.length <= 2) return parts.join(".") || "UnknownFrame";
  return parts.slice(-2).join(".");
}

function extractLocation(run) {
  const cp = run?.checkpoint;
  if (cp?.encounter_id) return cp.encounter_id;
  if (cp?.encounter) return cp.encounter;
  if (cp?.event_id) return cp.event_id;

  const state = run?.state;
  if (state?.combat?.encounter_id) return state.combat.encounter_id;
  if (state?.combat?.encounter) return state.combat.encounter;

  const route = arr(state?.route);
  if (route.length > 0) {
    const lastAct = route[route.length - 1];
    const points = arr(lastAct?.points);
    if (points.length > 0) {
      const lastPoint = points[points.length - 1];
      const rooms = arr(lastPoint?.rooms);
      if (rooms.length > 0) {
        const lastRoom = rooms[rooms.length - 1];
        if (lastRoom?.model_id) return lastRoom.model_id;
        if (lastRoom?.type) return lastRoom.type;
      }
      if (lastPoint?.map_point_type) return lastPoint.map_point_type;
    }
  }

  const monsters = arr(state?.combat?.monsters);
  if (monsters.length > 0 && monsters[0]?.id) return monsters[0].id;

  const events = arr(run?.events);
  for (let i = events.length - 1; i >= 0; i--) {
    const ev = events[i];
    if (typeof ev?.room === "string" && ev.room.trim()) return ev.room.trim();
  }

  if (cp?.room_type) return cp.room_type;
  if (state?.current_act) return state.current_act;
  return "general";
}

function extractNetworkType(run) {
  const candidates = [
    run?.network?.network_type,
    run?.network?.type,
    run?.network?.mode,
    run?.state?.network_type,
    run?.state?.network?.network_type,
    run?.state?.network?.type,
    run?.state?.game_mode,
  ];
  for (const c of candidates) {
    if (typeof c === "string" && c.trim()) return c.trim().slice(0, 80);
  }
  return "";
}

function collectFaults(run) {
  const faults = [];
  const eventSignatures = new Set();
  const eventFaults = [];

  for (const ev of arr(run?.events)) {
    if (!ev || typeof ev !== "object") continue;
    const kind = String(ev.kind || "");
    const detail = ev.detail;
    const detailText = typeof detail === "string" ? detail : (detail && typeof detail === "object" ? JSON.stringify(detail) : "");
    const isErrorKind = ["runtime_error", "task_fault", "exception", "unhandled_exception", "error"].includes(kind);
    const looksLikeStack = /\b[A-Za-z0-9_.]*Exception\b/.test(detailText) && (/\bat\s+[A-Za-z0-9_.]+/.test(detailText) || detailText.includes("PortraitPatch"));
    if (!isErrorKind && !looksLikeStack) continue;

    let exType = "";
    let message = "";
    let frames = [];
    let faultId = "";
    if (detail && typeof detail === "object") {
      exType = detail.exception_type || detail.type || detail.code || "";
      message = detail.message || detail.summary || "";
      faultId = detail.fault_id || "";
      frames = extractFramesFromStack(detail.frames || detail.stack || "", detail.top_frame || detail.member || "");
    } else if (typeof detail === "string") {
      const firstLine = detail.split(/\r?\n/)[0] || "";
      const exMatch = firstLine.match(/^([A-Za-z0-9_.]+(?:Exception|Error))\s*(?::\s*(.*))?$/);
      if (exMatch) {
        exType = exMatch[1];
        message = exMatch[2] || firstLine;
      } else {
        message = firstLine;
      }
      frames = extractFramesFromStack(detail);
    }
    const shortEx = shortExceptionName(exType, message || detailText);
    const sigKey = faultId || (shortEx + "|" + (frames[0]?.methodPath || message.slice(0, 80)));
    eventSignatures.add(sigKey);
    eventFaults.push({
      sigKey,
      faultId,
      exceptionType: shortEx,
      rawExceptionType: exType,
      message: message || detailText.slice(0, 500),
      frames,
      harmonyOwners: [],
      count: 1,
      fromPreviousSession: false,
      corroboratedByEvent: true,
    });
  }

  if (arr(run?.runtime_faults).length > 0) {
    for (const f of arr(run.runtime_faults)) {
      if (!f || typeof f !== "object") continue;
      let frames = extractFramesFromStack(f.stack, f.top_frame);
      let exType = f.exception_type || "";
      let msg = f.message || f.signature || "";
      // Unwrap inner exception details if outer exception has few frames or is a generic wrapper.
      for (const inner of arr(f.inner_exceptions)) {
        if (typeof inner === "string") {
          const innerFrames = extractFramesFromStack(inner);
          if (!frames.length && innerFrames.length) frames = innerFrames;
          if (!exType || /TargetInvocationException|AggregateException/i.test(exType)) {
            exType = shortExceptionName("", inner) || exType;
          }
        } else if (inner && typeof inner === "object") {
          const innerFrames = extractFramesFromStack(inner.stack, inner.top_frame);
          if (innerFrames.length) frames = [...innerFrames, ...frames];
          if (inner.exception_type && (!exType || /TargetInvocationException|AggregateException/i.test(exType))) {
            exType = inner.exception_type;
            msg = inner.message || msg;
          }
        }
      }
      const shortEx = shortExceptionName(exType, msg);
      const fromPrev = f.session === "previous" || f.session === "last_run" || f.from_previous_session === true;
      const count = Number.isInteger(f.count) && f.count > 0 ? f.count : 1;
      const corroborated = eventFaults.some(ef =>
        (f.fault_id && ef.faultId === f.fault_id)
        || (ef.exceptionType === shortEx && ef.frames.some(x => frames.some(y => y.methodPath === x.methodPath)))
      );
      faults.push({
        sigKey: f.signature || (shortEx + "|" + (frames[0]?.methodPath || msg.slice(0, 80))),
        exceptionType: shortEx,
        rawExceptionType: exType,
        message: msg,
        frames,
        harmonyOwners: arr(f.harmony_owners).filter(x => typeof x === "string"),
        count,
        fromPreviousSession: fromPrev,
        corroboratedByEvent: corroborated,
      });
    }
  } else if (arr(run?.runtime_errors).length > 0) {
    for (const e of arr(run.runtime_errors)) {
      if (!e || typeof e !== "object") continue;
      const frames = extractFramesFromStack(e.frames || e.stack, e.member);
      const msg = e.message || e.member || "";
      const shortEx = shortExceptionName(e.exception_type || e.type || e.code, msg);
      const corroborated = eventFaults.some(ef =>
        (e.fault_id && ef.faultId === e.fault_id)
        || (ef.exceptionType === shortEx && ef.frames.some(x => frames.some(y => y.methodPath === x.methodPath)))
      );
      faults.push({
        sigKey: e.fault_id || (shortEx + "|" + (frames[0]?.methodPath || msg.slice(0, 80))),
        exceptionType: shortEx,
        rawExceptionType: e.type || e.code || "",
        message: msg,
        frames,
        harmonyOwners: [],
        count: 1,
        fromPreviousSession: Boolean(e.from_previous_session),
        corroboratedByEvent: corroborated,
      });
    }
  }

  for (const ef of eventFaults) {
    const alreadyCovered = faults.some(f =>
      f.sigKey === ef.sigKey
      || (f.exceptionType === ef.exceptionType && ef.frames.length > 0 && f.frames.some(x => x.methodPath === ef.frames[0].methodPath))
    );
    if (!alreadyCovered) faults.push(ef);
  }

  return faults;
}

function buildModIndexes(run) {
  const activeMods = arr(run?.active_mods).filter(m => m && typeof m.id === "string");
  const externalMods = activeMods.filter(m => m.id !== "STS2_Things");
  const assemblies = arr(run?.assemblies).filter(a => a && typeof a.name === "string");

  const assemblyToMod = new Map();
  for (const a of assemblies) {
    const key = normToken(a.name.replace(/\.dll$/i, ""));
    if (key && a.mod_id) assemblyToMod.set(key, a.mod_id);
  }

  const harmonyByMethod = new Map();
  for (const hp of arr(run?.harmony_patches)) {
    if (!hp || typeof hp !== "object") continue;
    const targetRaw = hp.method || hp.target || (hp.declaring_type && hp.method_name ? hp.declaring_type + "." + hp.method_name : "");
    const parsed = parseStackFrame(String(targetRaw));
    const key = parsed ? parsed.methodPath : String(targetRaw || "").trim();
    if (!key) continue;
    const owners = new Set(harmonyByMethod.get(key) || []);
    for (const o of arr(hp.owners)) if (typeof o === "string" && o.trim()) owners.add(o.trim());
    for (const patchListKey of ["prefixes", "postfixes", "transpilers", "finalizers"]) {
      for (const item of arr(hp[patchListKey])) {
        if (typeof item === "string" && item.trim()) owners.add(item.trim());
        else if (item && typeof item.owner === "string" && item.owner.trim()) owners.add(item.owner.trim());
      }
    }
    if (owners.size > 0) harmonyByMethod.set(key, [...owners]);
  }

  return {activeMods, externalMods, assemblyToMod, harmonyByMethod};
}

function matchExternalModId(token, fullContext, indexes) {
  const norm = normToken(token);
  if (!norm) return "";
  if (norm === "sts2things") return "STS2_Things";

  if (indexes.assemblyToMod.has(norm)) {
    const mapped = indexes.assemblyToMod.get(norm);
    if (mapped && mapped !== "STS2_Things") return mapped;
  }

  for (const mod of indexes.externalMods) {
    const modNorm = normToken(mod.id);
    if (modNorm && (modNorm === norm || modNorm.includes(norm) || norm.includes(modNorm))) {
      return mod.id;
    }
  }

  if (/PortraitPatch/i.test(fullContext) || norm === "cardarteditor") {
    const portraitMod = indexes.externalMods.find(m => PORTRAIT_HINT.test((m.id || "") + " " + (m.name || "")));
    if (portraitMod) return portraitMod.id;
    const canonical = KNOWN_EXTERNAL_CANONICAL.get(norm) || token;
    return "external:" + canonical;
  }

  if (KNOWN_EXTERNAL_CANONICAL.has(norm)) {
    return "external:" + KNOWN_EXTERNAL_CANONICAL.get(norm);
  }

  return "external:" + token;
}

function lookupHarmonyOwners(methodPath, harmonyByMethod) {
  if (harmonyByMethod.has(methodPath)) return harmonyByMethod.get(methodPath);
  const tail = shortMethodLabel(methodPath);
  for (const [k, owners] of harmonyByMethod.entries()) {
    if (k === methodPath || k.endsWith("." + tail) || methodPath.endsWith("." + k)) {
      return owners;
    }
  }
  return [];
}

function analyzeSingleFault(fault, indexes) {
  const nonWrapper = fault.frames.filter(f => !isWrapperFrame(f.methodPath));
  const specificFrame = nonWrapper.find(f => !isEngineOrBclFrame(f.methodPath)) || nonWrapper[0] || fault.frames[0] || null;
  const culpritPath = specificFrame ? specificFrame.methodPath : "UnknownFrame";
  let attributedModId = "";
  let category = "game_runtime_error";
  let attributionFrame = culpritPath;

  for (const frame of nonWrapper) {
    const mp = frame.methodPath;
    if (/^STS2_Things\./i.test(mp)) {
      attributedModId = "STS2_Things";
      category = "mod_runtime_error";
      attributionFrame = mp;
      break;
    }
    const rootNs = mp.split(".")[0] || "";
    const rootLower = rootNs.toLowerCase();
    if (rootNs && !VANILLA_OR_ENGINE_ROOTS.has(rootLower)) {
      attributedModId = matchExternalModId(rootNs, mp + " " + fault.message, indexes);
      category = "external_mod_conflict";
      attributionFrame = mp;
      break;
    }
    const patchedOwners = lookupHarmonyOwners(mp, indexes.harmonyByMethod);
    if (patchedOwners.length > 0 || frame.hadHarmonyMarker) {
      const owners = patchedOwners.length > 0 ? patchedOwners : fault.harmonyOwners;
      const extOwner = owners.find(o => normToken(o) !== "sts2things" && !VANILLA_OR_ENGINE_ROOTS.has(o.toLowerCase()));
      if (extOwner) {
        attributedModId = matchExternalModId(extOwner, mp + " " + fault.message, indexes);
        category = "external_mod_conflict";
        attributionFrame = mp;
        break;
      }
      if (owners.some(o => normToken(o) === "sts2things" || /^STS2_Things/i.test(o))) {
        attributedModId = "STS2_Things";
        category = "mod_runtime_error";
        attributionFrame = mp;
        break;
      }
    }
  }

  if (!attributedModId && fault.harmonyOwners.length > 0) {
    const extOwner = fault.harmonyOwners.find(o => normToken(o) !== "sts2things" && !VANILLA_OR_ENGINE_ROOTS.has(o.toLowerCase()));
    if (extOwner) {
      attributedModId = matchExternalModId(extOwner, culpritPath + " " + fault.message, indexes);
      category = "external_mod_conflict";
    } else if (fault.harmonyOwners.some(o => normToken(o) === "sts2things" || /^STS2_Things/i.test(o))) {
      attributedModId = "STS2_Things";
      category = "mod_runtime_error";
    }
  }

  if (!attributedModId) {
    const combinedText = fault.message + " " + fault.rawExceptionType + " " + fault.frames.map(f => f.raw).join(" ");
    if (/CardArtEditor\.PortraitPatch|PortraitPatch\.(?:RestoreDefaultState|TryReplacePortrait|UpdateVisuals)/i.test(combinedText)
      || (fault.exceptionType === "ObjectDisposedException" && /PortraitPatch/i.test(combinedText))) {
      attributedModId = matchExternalModId("CardArtEditor", combinedText, indexes);
      category = "external_mod_conflict";
    } else {
      for (const [normKey, canonicalName] of KNOWN_EXTERNAL_CANONICAL.entries()) {
        if (combinedText.toLowerCase().includes(normKey)) {
          attributedModId = matchExternalModId(canonicalName, combinedText, indexes);
          category = "external_mod_conflict";
          break;
        }
      }
    }
  }

  return {
    fault,
    culpritPath: attributionFrame || culpritPath,
    shortCulprit: shortMethodLabel(attributionFrame || culpritPath),
    attributedModId,
    category,
  };
}

function choosePrimaryFault(analyzedList) {
  if (analyzedList.length === 0) return null;
  // Priority score: current-session + event-corroborated > current-session > event-corroborated > mod-attributed > latest.
  let best = analyzedList[0];
  let bestScore = -1;
  for (let i = 0; i < analyzedList.length; i++) {
    const item = analyzedList[i];
    const f = item.fault;
    let score = 0;
    if (!f.fromPreviousSession) score += 100;
    if (f.corroboratedByEvent) score += 80;
    if (item.attributedModId) score += 20;
    if (item.culpritPath !== "UnknownFrame") score += 10;
    // Slight tie-breaker favoring later (more recent) faults in run timeline.
    score += Math.min(9, i * 0.1);
    if (score >= bestScore) {
      bestScore = score;
      best = item;
    }
  }
  return best;
}

export function analyzeReport(payload) {
  const run = payload?.run || {};
  const schema_version = Number.isInteger(run.schema)
    ? run.schema
    : (Number.isInteger(payload?.schema) ? payload.schema : 1);

  const has_checkpoint = run.checkpoint?.available === true ? 1 : 0;
  const has_screenshot = run.screenshot?.available === true ? 1 : 0;
  const has_replay = run.combat_replay?.available === true ? 1 : 0;
  const has_desync = run.desync?.detected === true ? 1 : 0;
  const network_type = extractNetworkType(run);
  const location = extractLocation(run);

  const faults = collectFaults(run);
  const runtime_error_count = faults.reduce((sum, f) => sum + (f.count || 1), 0);
  const indexes = buildModIndexes(run);
  const analyzedFaults = faults.map(f => analyzeSingleFault(f, indexes));
  const primary = choosePrimaryFault(analyzedFaults);

  if (has_desync) {
    const locKey = sanitizeKey(location, "combat");
    const cluster_key = "desync:" + locKey;
    const cluster_title = "联机不同步 · " + location;
    const attributed_mod_id = primary?.attributedModId || "";
    const issue_fingerprint = sha256Hex("desync|" + cluster_key).slice(0, 16);
    return {
      schema_version,
      issue_fingerprint,
      cluster_key,
      cluster_title,
      category: "multiplayer_desync",
      attributed_mod_id,
      network_type,
      has_checkpoint,
      has_screenshot,
      has_replay,
      has_desync,
      runtime_error_count,
    };
  }

  if (primary) {
    const ex = sanitizeKey(primary.fault.exceptionType, "RuntimeError");
    const methodKey = sanitizeKey(primary.shortCulprit, "UnknownFrame");
    const category = primary.category;
    const attributed_mod_id = primary.attributedModId;
    let cluster_key;
    let cluster_title;
    if (category === "external_mod_conflict") {
      const displayMod = attributed_mod_id.replace(/^external:/, "") || "ExternalMod";
      cluster_key = `ext:${sanitizeKey(displayMod)}:${ex}:${methodKey}`;
      cluster_title = `[外部模组 ${displayMod}] ${primary.fault.exceptionType} @ ${primary.shortCulprit}`;
    } else if (attributed_mod_id === "STS2_Things") {
      cluster_key = `things:${ex}:${methodKey}`;
      cluster_title = `[STS2_Things] ${primary.fault.exceptionType} @ ${primary.shortCulprit}`;
    } else {
      cluster_key = `fault:${ex}:${methodKey}`;
      cluster_title = `${primary.fault.exceptionType} @ ${primary.shortCulprit}`;
    }
    const issue_fingerprint = sha256Hex(
      `fault|${category}|${attributed_mod_id}|${primary.fault.exceptionType}|${primary.culpritPath}`
    ).slice(0, 16);
    return {
      schema_version,
      issue_fingerprint,
      cluster_key: cluster_key.slice(0, 240),
      cluster_title: cluster_title.slice(0, 280),
      category,
      attributed_mod_id,
      network_type,
      has_checkpoint,
      has_screenshot,
      has_replay,
      has_desync,
      runtime_error_count,
    };
  }

  const locKey = sanitizeKey(location, "general");
  const cluster_key = "manual:" + locKey;
  const cluster_title = "手动反馈 · " + location;
  const issue_fingerprint = sha256Hex("manual|" + cluster_key).slice(0, 16);
  return {
    schema_version,
    issue_fingerprint,
    cluster_key,
    cluster_title,
    category: "gameplay_feedback",
    attributed_mod_id: "",
    network_type,
    has_checkpoint,
    has_screenshot,
    has_replay,
    has_desync,
    runtime_error_count: 0,
  };
}
