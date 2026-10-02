// Cloudflare Pages Function: /i/:id
// Retrieves character avatars from Cloudflare R2 with immutable edge caching

export async function onRequestGet(context) {
  const { params, env } = context;
  const key = params.id;

  if (!key) {
    return new Response("File not specified", { status: 400 });
  }

  if (!env.BUCKET) {
    return new Response("R2 bucket binding 'BUCKET' is not configured.", { status: 500 });
  }

  const object = await env.BUCKET.get(key);
  if (!object) {
    return new Response("Image Not Found", { status: 404 });
  }

  const headers = new Headers();
  object.writeHttpMetadata(headers);
  headers.set("etag", object.httpEtag);
  headers.set("Cache-Control", "public, max-age=31536000, immutable");
  headers.set("Access-Control-Allow-Origin", "*");

  return new Response(object.body, { headers });
}
