FROM python:3.11-slim

WORKDIR /app

# curl is used by the docker-compose healthcheck.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Install the CPU-only build of torch first so we don't pull the ~2GB CUDA build
# that sentence-transformers would otherwise drag in as a transitive dependency.
RUN pip install --no-cache-dir torch --index-url https://download.pytorch.org/whl/cpu

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY app.py .

# Bake the model weights into the image so the container is ready on first
# request rather than downloading ~600MB at runtime.
ENV CLIP_MODEL=clip-ViT-B-32
RUN python -c "import os; from sentence_transformers import SentenceTransformer; SentenceTransformer(os.getenv('CLIP_MODEL'))"

EXPOSE 8000
CMD ["uvicorn", "app:app", "--host", "0.0.0.0", "--port", "8000"]
