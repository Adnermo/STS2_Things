import { notFound } from "./utils";
import { RequestHandlerParams } from "./utils";

export async function handleRequestGet({
  bucket,
  path,
  request,
}: RequestHandlerParams) {
  const obj = await bucket.get(path, {
    onlyIf: request.headers,
    range: request.headers,
  });
  if (obj === null) return notFound();
  if (!("body" in obj)) {
    const isConditionalGet =
      request.headers.has("if-none-match") ||
      request.headers.has("if-modified-since");
    if (isConditionalGet) {
      const notModifiedHeaders = new Headers();
      obj.writeHttpMetadata(notModifiedHeaders);
      notModifiedHeaders.set("etag", obj.httpEtag);
      notModifiedHeaders.set("Last-Modified", obj.uploaded.toUTCString());
      return new Response(null, { status: 304, headers: notModifiedHeaders });
    }
    return new Response("Preconditions failed", { status: 412 });
  }

  const headers = new Headers();
  obj.writeHttpMetadata(headers);
  headers.set("etag", obj.httpEtag);
  headers.set("Accept-Ranges", "bytes");
  headers.set("Last-Modified", obj.uploaded.toUTCString());
  if (path.startsWith("_$flaredrive$/thumbnails/"))
    headers.set("Cache-Control", "max-age=31536000");

  if (request.headers.has("range") && obj.range) {
    let start = 0;
    let end = obj.size - 1;

    if ("suffix" in obj.range && obj.range.suffix !== undefined) {
      const suffix = Math.min(obj.range.suffix, obj.size);
      start = Math.max(0, obj.size - suffix);
      end = obj.size - 1;
    } else {
      const offset =
        "offset" in obj.range && obj.range.offset !== undefined
          ? obj.range.offset
          : 0;
      const length =
        "length" in obj.range && obj.range.length !== undefined
          ? obj.range.length
          : obj.size - offset;
      start = offset;
      end = Math.min(obj.size - 1, offset + length - 1);
    }

    if (obj.size === 0 || start >= obj.size || end < start) {
      headers.set("Content-Range", `bytes */${obj.size}`);
      return new Response(null, { status: 416, headers });
    }

    headers.set("Content-Range", `bytes ${start}-${end}/${obj.size}`);
    headers.set("Content-Length", (end - start + 1).toString());
    return new Response(obj.body, { status: 206, headers });
  }

  headers.set("Content-Length", obj.size.toString());
  return new Response(obj.body, { headers });
}
