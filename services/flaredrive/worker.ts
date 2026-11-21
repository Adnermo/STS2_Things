import { onRequest as webdavOnRequest } from "./functions/webdav/[[path]]";

interface Env {
  BUCKET: R2Bucket;
  ASSETS: Fetcher;
  WEBDAV_USERNAME: string;
  WEBDAV_PASSWORD: string;
  WEBDAV_PUBLIC_READ?: string;
  [key: string]: unknown;
}

interface LatestMeta {
  version?: string;
  tag?: string;
  zip?: string;
  updatedAt?: string;
}

interface SemVerKey {
  major: number;
  minor: number;
  patch: number;
  stable: number;
  pre: string;
}

const STATIC_ASSET_EXT_RE =
  /\.(js|css|map|png|jpg|jpeg|gif|svg|ico|webp|woff2?|ttf|eot|json|txt|xml)$/i;

function parseSemVerKey(tag: string): SemVerKey | null {
  const clean = tag.replace(/^[vV]/, "").trim();
  const match = clean.match(/^(\d+)\.(\d+)\.(\d+)(?:-(.+))?$/);
  if (!match) return null;
  return {
    major: Number(match[1]),
    minor: Number(match[2]),
    patch: Number(match[3]),
    stable: match[4] ? 0 : 1,
    pre: match[4] ?? "",
  };
}

function compareSemVerDesc(a: string, b: string): number {
  const pa = parseSemVerKey(a);
  const pb = parseSemVerKey(b);
  if (pa && !pb) return -1;
  if (!pa && pb) return 1;
  if (!pa && !pb) return b.localeCompare(a);
  if (pa!.major !== pb!.major) return pb!.major - pa!.major;
  if (pa!.minor !== pb!.minor) return pb!.minor - pa!.minor;
  if (pa!.patch !== pb!.patch) return pb!.patch - pa!.patch;
  if (pa!.stable !== pb!.stable) return pb!.stable - pa!.stable;
  return pb!.pre.localeCompare(pa!.pre);
}

async function findZipInVersionDir(
  bucket: R2Bucket,
  tag: string
): Promise<string | null> {
  const prefix = `STS2_Things/${tag}/`;
  let cursor: string | undefined;
  let fallbackZip: string | null = null;
  do {
    const listed = await bucket.list({ prefix, delimiter: "/", cursor });
    for (const obj of listed.objects) {
      const name = obj.key.slice(prefix.length);
      if (name.toLowerCase().endsWith(".zip")) {
        if (name.toLowerCase().includes("unified")) {
          return name;
        }
        if (!fallbackZip) fallbackZip = name;
      }
    }
    cursor = listed.truncated ? listed.cursor : undefined;
  } while (cursor);
  return fallbackZip;
}

async function resolveLatestRelease(
  bucket: R2Bucket
): Promise<{ tag: string; zip: string | null } | null> {
  try {
    const metaObj = await bucket.get("STS2_Things/_latest.json");
    if (metaObj) {
      const meta = (await metaObj.json()) as LatestMeta;
      const rawTag = meta.tag || (meta.version ? `v${meta.version}` : "");
      if (rawTag) {
        const tag = rawTag.startsWith("v") ? rawTag : `v${rawTag}`;
        const zip = meta.zip || (await findZipInVersionDir(bucket, tag));
        return { tag, zip };
      }
    }
  } catch {
    // Fall back to scanning STS2_Things/v* directories below
  }

  const versionTags = new Set<string>();
  let cursor: string | undefined;
  do {
    const listed = await bucket.list({
      prefix: "STS2_Things/",
      delimiter: "/",
      cursor,
      include: ["httpMetadata"],
    });
    for (const obj of listed.objects) {
      const name = obj.key.slice("STS2_Things/".length);
      if (
        name.startsWith("v") &&
        obj.httpMetadata?.contentType === "application/x-directory"
      ) {
        versionTags.add(name);
      }
    }
    for (const dp of listed.delimitedPrefixes) {
      const name = dp.slice("STS2_Things/".length).replace(/\/$/, "");
      if (name.startsWith("v")) {
        versionTags.add(name);
      }
    }
    cursor = listed.truncated ? listed.cursor : undefined;
  } while (cursor);

  const sorted = Array.from(versionTags).sort(compareSemVerDesc);
  if (sorted.length === 0) return null;

  const latestTag = sorted[0];
  const zip = await findZipInVersionDir(bucket, latestTag);
  return { tag: latestTag, zip };
}

const worker: ExportedHandler<Env> = {
  async fetch(request, env, ctx): Promise<Response> {
    const url = new URL(request.url);
    const pathname = url.pathname;

    // 1. Upstream FlareDrive WebDAV + Multipart Upload + Public Read handler
    if (pathname === "/webdav" || pathname.startsWith("/webdav/")) {
      const rawSubpath = pathname.slice("/webdav".length);
      const pathSegments = rawSubpath.split("/").filter(Boolean);
      return webdavOnRequest({
        request,
        env,
        params: { path: pathSegments },
        waitUntil: ctx.waitUntil.bind(ctx),
        passThroughOnException: ctx.passThroughOnException.bind(ctx),
        data: {},
        next: () => Promise.resolve(new Response("Not found", { status: 404 })),
        functionPath: "/webdav/[[path]]",
      } as unknown as Parameters<typeof webdavOnRequest>[0]);
    }

    // 2. Convenience redirects for latest STS2_Things release
    if (request.method === "GET" || request.method === "HEAD") {
      if (
        pathname === "/STS2_Things/latest" ||
        pathname === "/STS2_Things/latest/" ||
        pathname === "/latest" ||
        pathname === "/latest/"
      ) {
        const latest = await resolveLatestRelease(env.BUCKET);
        if (!latest) {
          return new Response("No STS2_Things release found", { status: 404 });
        }
        return new Response(null, {
          status: 302,
          headers: {
            Location: `/STS2_Things/${encodeURIComponent(latest.tag)}/`,
            "Cache-Control": "no-store",
          },
        });
      }

      if (
        pathname === "/STS2_Things/latest.zip" ||
        pathname === "/latest.zip"
      ) {
        const latest = await resolveLatestRelease(env.BUCKET);
        if (!latest || !latest.zip) {
          return new Response("No latest STS2_Things ZIP found", {
            status: 404,
          });
        }
        return new Response(null, {
          status: 302,
          headers: {
            Location: `/webdav/STS2_Things/${encodeURIComponent(
              latest.tag
            )}/${encodeURIComponent(latest.zip)}`,
            "Cache-Control": "no-store",
          },
        });
      }
    }

    // 3. Static FlareDrive React build assets + SPA fallback for folder URLs
    const assetResponse = await env.ASSETS.fetch(request);
    if (
      assetResponse.status !== 404 ||
      (request.method !== "GET" && request.method !== "HEAD")
    ) {
      return assetResponse;
    }

    if (
      pathname.startsWith("/static/") ||
      STATIC_ASSET_EXT_RE.test(pathname)
    ) {
      return assetResponse;
    }

    const indexUrl = new URL("/index.html", request.url);
    return env.ASSETS.fetch(
      new Request(indexUrl.toString(), {
        method: request.method,
        headers: request.headers,
      })
    );
  },
};

export default worker;
