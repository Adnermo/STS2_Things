import { notFound } from "./utils";
import { RequestHandlerParams } from "./utils";

export async function handleRequestHead({
  bucket,
  path,
}: RequestHandlerParams) {
  const obj = await bucket.head(path);
  if (obj === null) return notFound();

  const headers = new Headers();
  obj.writeHttpMetadata(headers);
  headers.set("etag", obj.httpEtag);
  headers.set("Content-Length", obj.size.toString());
  headers.set("Accept-Ranges", "bytes");
  headers.set("Last-Modified", obj.uploaded.toUTCString());
  return new Response(null, { headers });
}
