"""
CLIP image-embedding sidecar for Shopit visual search.

A tiny FastAPI service that wraps a SentenceTransformer CLIP model and turns raw
image bytes into an embedding vector — the self-hosted equivalent of Azure
Computer Vision's vectorizeImage / the Hugging Face feature-extraction API, with
no API key, no per-call cost, and no external rate limits.

The C# side (ClipImageEmbeddingService) POSTs raw image bytes to /embed and reads
back {"vector": [...], "model": "..."}. The same model must embed both the indexed
catalog images and the query photo, so /embed echoes the model name back as the
version the C# side stores for comparability.
"""

import io
import os

from fastapi import FastAPI, HTTPException, Request
from PIL import Image
from sentence_transformers import SentenceTransformer

MODEL_NAME = os.getenv("CLIP_MODEL", "clip-ViT-B-32")

# Loaded once at import time so the first request isn't a cold model load. The
# compose healthcheck below waits for /health before the api service starts.
model = SentenceTransformer(MODEL_NAME)

app = FastAPI(title="Shopit CLIP embedding sidecar")


@app.get("/health")
def health():
    return {"status": "ok", "model": MODEL_NAME}


@app.post("/embed")
async def embed(request: Request):
    body = await request.body()
    if not body:
        raise HTTPException(status_code=400, detail="Empty image body.")

    try:
        image = Image.open(io.BytesIO(body)).convert("RGB")
    except Exception:
        raise HTTPException(status_code=400, detail="Unsupported or corrupt image.")

    vector = model.encode(image, normalize_embeddings=False).tolist()
    return {"vector": vector, "model": MODEL_NAME}
