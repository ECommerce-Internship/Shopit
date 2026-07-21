using Shopit.Domain.Entities;

namespace Shopit.Application.DTOs.Rag;

/// <summary>
/// A retrieved <see cref="DocumentChunk"/> paired with its similarity score
/// against the query (1.0 = identical direction, ~0 = unrelated).
/// </summary>
public record ScoredChunk(DocumentChunk Chunk, double Score);
