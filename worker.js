// Stardew Presence - Cloudflare Worker & API
// Handles dynamic character avatar uploads, edge caching in R2, and serves the web dashboard

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    // 1. CORS Preflight
    if (request.method === "OPTIONS") {
      return new Response(null, {
        headers: {
          "Access-Control-Allow-Origin": "*",
          "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
          "Access-Control-Allow-Headers": "Content-Type, Authorization",
        },
      });
    }

    const corsHeaders = {
      "Access-Control-Allow-Origin": "*",
      "Content-Type": "application/json",
    };

    // 2. Health Check / Status
    if (request.method === "GET" && (url.pathname === "/api/upload" || url.pathname === "/api/health")) {
      return new Response(JSON.stringify({
        service: "Stardew Presence Image Hosting API",
        status: "operational",
        bucketConfigured: Boolean(env.BUCKET)
      }), {
        status: 200,
        headers: corsHeaders
      });
    }

    // 3. POST /api/upload or POST /upload
    if (request.method === "POST" && (url.pathname === "/api/upload" || url.pathname === "/upload")) {
      try {
        if (!env.BUCKET) {
          return new Response(JSON.stringify({
            error: "R2 bucket is not bound. Bind your R2 bucket as 'BUCKET' in Cloudflare settings or wrangler.toml."
          }), {
            status: 500,
            headers: corsHeaders
          });
        }

        let imageBytes = null;
        const contentType = request.headers.get("content-type") || "";

        if (contentType.includes("multipart/form-data")) {
          const formData = await request.formData();
          const file = formData.get("file") || formData.get("image");
          if (!file || typeof file === "string") {
            return new Response(JSON.stringify({ error: "Missing 'file' field in multipart form." }), {
              status: 400,
              headers: corsHeaders
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
          } catch {
            imageBytes = await request.arrayBuffer();
          }
        }

        if (!imageBytes || imageBytes.byteLength === 0) {
          return new Response(JSON.stringify({ error: "Image byte payload is empty." }), {
            status: 400,
            headers: corsHeaders
          });
        }

        // SHA-256 for deterministic caching and deduplication
        const hashBuffer = await crypto.subtle.digest("SHA-256", imageBytes);
        const hashArray = Array.from(new Uint8Array(hashBuffer));
        const hashHex = hashArray.map(b => b.toString(16).padStart(2, "0")).join("").substring(0, 32);
        const fileName = `${hashHex}.png`;

        await env.BUCKET.put(fileName, imageBytes, {
          httpMetadata: {
            contentType: "image/png",
            cacheControl: "public, max-age=31536000, immutable"
          }
        });

        const publicUrl = `${url.origin}/i/${fileName}`;

        return new Response(JSON.stringify({
          success: true,
          url: publicUrl,
          filename: fileName,
          bytes: imageBytes.byteLength
        }), {
          status: 200,
          headers: corsHeaders
        });
      } catch (err) {
        return new Response(JSON.stringify({
          error: err.message || "Failed to process image upload."
        }), {
          status: 500,
          headers: corsHeaders
        });
      }
    }

    // 4. GET or HEAD /i/:id - Serve image from R2 with edge caching
    if ((request.method === "GET" || request.method === "HEAD") && url.pathname.startsWith("/i/")) {
      const key = url.pathname.slice(3);
      if (!key) {
        return new Response("Not found", { status: 404 });
      }

      if (!env.BUCKET) {
        return new Response("R2 BUCKET binding not configured.", { status: 500 });
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

      if (request.method === "HEAD") {
        return new Response(null, { headers });
      }

      return new Response(object.body, { headers });
    }

    // 5. Serve static web dashboard via Workers Assets
    if (env.ASSETS) {
      return env.ASSETS.fetch(request);
    }

    return new Response("Stardew Presence Image Hosting Worker is Running!", {
      status: 200,
      headers: { "Content-Type": "text/plain" }
    });
  }
};
