// Stardew Presence - Image Hosting Worker
// Stores rendered character portraits in Cloudflare R2


export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    // Handle CORS preflight
    if (request.method === "OPTIONS") {
      return new Response(null, {
        headers: {
          "Access-Control-Allow-Origin": "*",
          "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
          "Access-Control-Allow-Headers": "Content-Type, Authorization",
        },
      });
    }

    // POST /api/upload or POST /upload or POST /
    if (request.method === "POST" && (url.pathname === "/api/upload" || url.pathname === "/upload" || url.pathname === "/")) {
      try {
        if (!env.BUCKET) {
          return new Response(JSON.stringify({ error: "R2 BUCKET binding is not configured in Worker settings." }), {
            status: 500,
            headers: { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" },
          });
        }

        let imageBytes = null;
        const contentType = request.headers.get("content-type") || "";

        if (contentType.includes("multipart/form-data")) {
          const formData = await request.formData();
          const file = formData.get("file") || formData.get("image");
          if (!file || typeof file === "string") {
            return new Response(JSON.stringify({ error: "Missing image file in multipart 'file' field." }), {
              status: 400,
              headers: { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" },
            });
          }
          imageBytes = await file.arrayBuffer();
        } else if (contentType.includes("image/png") || contentType.includes("application/octet-stream")) {
          imageBytes = await request.arrayBuffer();
        } else {
          try {
            const formData = await request.formData();
            const file = formData.get("file") || formData.get("image");
            if (file && typeof file !== "string") {
              imageBytes = await file.arrayBuffer();
            }
          } catch {}
        }

        if (!imageBytes || imageBytes.byteLength === 0) {
          return new Response(JSON.stringify({ error: "Image byte payload is empty." }), {
            status: 400,
            headers: { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" },
          });
        }

        // Deduplicate using SHA-256 hash of image content
        const hashBuffer = await crypto.subtle.digest("SHA-256", imageBytes);
        const hashArray = Array.from(new Uint8Array(hashBuffer));
        const hashHex = hashArray.map((b) => b.toString(16).padStart(2, "0")).join("").substring(0, 32);
        const fileName = `${hashHex}.png`;

        // Store in Cloudflare R2
        await env.BUCKET.put(fileName, imageBytes, {
          httpMetadata: {
            contentType: "image/png",
            cacheControl: "public, max-age=31536000, immutable",
          },
        });

        // Return direct URL
        const publicUrl = `${url.origin}/i/${fileName}`;

        return new Response(JSON.stringify({ url: publicUrl, success: true }), {
          status: 200,
          headers: {
            "Content-Type": "application/json",
            "Access-Control-Allow-Origin": "*",
          },
        });
      } catch (err) {
        return new Response(JSON.stringify({ error: err.message || "Failed to process image upload." }), {
          status: 500,
          headers: { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" },
        });
      }
    }

    // GET /i/:filename or GET /:filename
    if (request.method === "GET") {
      let key = url.pathname.replace(/^\/i\//, "").replace(/^\//, "");
      if (!key || key === "favicon.ico") {
        return new Response("Stardew Presence Cloudflare Image Hosting Worker is Running!", {
          status: 200,
          headers: { "Content-Type": "text/plain" },
        });
      }

      if (!env.BUCKET) {
        return new Response("R2 BUCKET binding is not configured.", { status: 500 });
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

    return new Response("Method Not Allowed", { status: 405 });
  },
};
