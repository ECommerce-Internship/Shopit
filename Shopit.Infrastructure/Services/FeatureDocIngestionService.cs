using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shopit.Application.DTOs.Rag;
using Shopit.Application.Rag;
using Shopit.Domain.Entities;
using Shopit.Infrastructure.Data;

namespace Shopit.Infrastructure.Services;

/// <summary>
/// Ingests the feature documentation Markdown files into the
/// <see cref="DocumentChunk"/> corpus (SCRUM-166): enumerate → chunk → embed →
/// upsert. Re-running is idempotent — a chunk whose content hash is unchanged is
/// left as-is and not re-embedded (the embedding cache), so a re-index after
/// editing one doc only spends embedding calls on what actually changed. Chunks
/// for files or sections that no longer exist are removed.
/// </summary>
public class FeatureDocIngestionService : IFeatureDocIngestionService
{
    private readonly AppDbContext _db;
    private readonly IEmbeddingService _embeddingService;
    private readonly MarkdownFeatureChunker _chunker;
    private readonly ILogger<FeatureDocIngestionService> _logger;
    private readonly string _docsPath;

    public FeatureDocIngestionService(
        AppDbContext db,
        IEmbeddingService embeddingService,
        MarkdownFeatureChunker chunker,
        IConfiguration configuration,
        ILogger<FeatureDocIngestionService> logger)
    {
        _db = db;
        _embeddingService = embeddingService;
        _chunker = chunker;
        _logger = logger;
        _docsPath = string.IsNullOrWhiteSpace(configuration["FeatureQa:DocsPath"])
            ? "docs/features"
            : configuration["FeatureQa:DocsPath"]!;
    }

    public async Task<IngestionResult> ReindexAsync(CancellationToken cancellationToken = default)
    {
        var docsRoot = ResolveDocsRoot(_docsPath);
        if (!Directory.Exists(docsRoot))
        {
            // Missing docs must not crash startup — log and no-op.
            _logger.LogWarning("Feature docs directory not found at {DocsRoot}; skipping ingestion.", docsRoot);
            return new IngestionResult(0, 0, 0, 0);
        }

        var files = Directory.GetFiles(docsRoot, "*.md", SearchOption.AllDirectories);

        var existing = await _db.DocumentChunks.ToListAsync(cancellationToken);
        var existingByKey = existing.ToDictionary(c => (c.SourceFile, c.Section));
        var seenKeys = new HashSet<(string, string)>();

        int totalChunks = 0, embedded = 0, skipped = 0;

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(docsRoot, file).Replace('\\', '/');
            var audience = relativePath.Split('/')[0];
            var sourceFile = $"{_docsPath.TrimEnd('/')}/{relativePath}";

            var markdown = await File.ReadAllTextAsync(file, cancellationToken);
            var parsed = _chunker.Chunk(markdown);

            foreach (var chunk in parsed)
            {
                totalChunks++;
                var key = (sourceFile, chunk.Section);
                seenKeys.Add(key);

                if (existingByKey.TryGetValue(key, out var row) && row.ContentHash == chunk.ContentHash)
                {
                    // Unchanged — keep the stored embedding, spend no Gemini call.
                    skipped++;
                    continue;
                }

                var embedding = await _embeddingService.EmbedAsync(chunk.Content, cancellationToken);
                embedded++;

                if (row is null)
                {
                    _db.DocumentChunks.Add(new DocumentChunk
                    {
                        FeatureName = chunk.FeatureName,
                        Section = chunk.Section,
                        Audience = audience,
                        SourceFile = sourceFile,
                        Content = chunk.Content,
                        ContentHash = chunk.ContentHash,
                        Embedding = embedding,
                        UpdatedAt = DateTime.UtcNow,
                    });
                }
                else
                {
                    row.FeatureName = chunk.FeatureName;
                    row.Audience = audience;
                    row.Content = chunk.Content;
                    row.ContentHash = chunk.ContentHash;
                    row.Embedding = embedding;
                    row.UpdatedAt = DateTime.UtcNow;
                }
            }
        }

        // Remove chunks whose file/section no longer exists in the docs.
        var stale = existing.Where(c => !seenKeys.Contains((c.SourceFile, c.Section))).ToList();
        if (stale.Count > 0)
            _db.DocumentChunks.RemoveRange(stale);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Feature-doc ingestion complete: {Files} files, {Chunks} chunks, {Embedded} embedded, {Skipped} unchanged, {Removed} removed.",
            files.Length, totalChunks, embedded, skipped, stale.Count);

        return new IngestionResult(files.Length, totalChunks, embedded, skipped);
    }

    /// <summary>
    /// Resolves the configured docs path: absolute paths are used as-is; relative
    /// paths are resolved against the app base directory, where the docs are
    /// copied at build time (see Shopit.API.csproj), falling back to the current
    /// directory for a dev run from the repo root.
    /// </summary>
    private static string ResolveDocsRoot(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
            return configuredPath;

        var fromBase = Path.Combine(AppContext.BaseDirectory, configuredPath);
        if (Directory.Exists(fromBase))
            return fromBase;

        return Path.Combine(Directory.GetCurrentDirectory(), configuredPath);
    }
}
