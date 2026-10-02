// Cloudflare Pages Function: /api/upload
// Handles character avatar uploads and stores them in Cloudflare R2

export async function onRequestPost(context) {
  const { request, env } = context;
  const url = new URL(request.url);

  const corsHeaders = {
    "Access-Control-Allow-Origin": "*",
    "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
    "Access-Control-Allow-Headers": "Content-Type, Authorization",
    "Content-Type": "application/json"
  };

  try {
    if (!env.BUCKET) {
      return new Response(JSON.stringify({
        error: "R2 bucket is not bound. In Cloudflare Pages Settings -> Functions -> R2 Bucket Bindings, bind your bucket as 'BUCKET'."
      }), {
        status: 500,
        headers: corsHeaders
      });
    }

    let imageBytes = null;
    const contentType = request.headers.get("content-type") || "";

    if (contentType.includes("multipart/form-data")) {
      try {
        const formData = await request.formData();
        const file = formData.get("file") || formData.get("image");
        if (file && typeof file !== "string") {
          imageBytes = await file.arrayBuffer();
        }
      } catch (e) {
        const raw = await request.arrayBuffer();
        const bytes = new Uint8Array(raw);
        const pngHeader = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
        let start = -1;
        for (let i = 0; i <= bytes.length - 8; i++) {
          if (pngHeader.every((b, idx) => bytes[i + idx] === b)) {
            start = i;
            break;
          }
        }
        if (start !== -1) {
          const iend = [0x49, 0x45, 0x4e, 0x44];
          let end = bytes.length;
          for (let i = start; i <= bytes.length - 8; i++) {
            if (iend.every((b, idx) => bytes[i + idx] === b)) {
              end = i + 8;
              break;
            }
          }
          imageBytes = raw.slice(start, end);
        }
      }
    } else if (contentType.includes("image/png") || contentType.includes("application/octet-stream")) {
      imageBytes = await request.arrayBuffer();
    } else {
      imageBytes = await request.arrayBuffer();
    }

    if (!imageBytes || imageBytes.byteLength === 0) {
      return new Response(JSON.stringify({ error: "Image byte payload is empty." }), {
        status: 400,
        headers: corsHeaders
      });
    }

    // Hash for deduplication and immutable cache key
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

export async function onRequestGet(context) {
  const { env } = context;
  return new Response(JSON.stringify({
    service: "Stardew Presence Image Hosting API",
    status: "operational",
    bucketConfigured: Boolean(env.BUCKET)
  }), {
    status: 200,
    headers: {
      "Content-Type": "application/json",
      "Access-Control-Allow-Origin": "*"
    }
  });
}

export async function onRequestOptions() {
  return new Response(null, {
    headers: {
      "Access-Control-Allow-Origin": "*",
      "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
      "Access-Control-Allow-Headers": "Content-Type, Authorization"
    }
  });
}
