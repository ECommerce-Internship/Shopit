namespace Shopit.Application.DTOs.Rag;

/// <summary>
/// Summary of a feature-doc ingestion run (SCRUM-166), returned by the admin
/// re-index endpoint and logged on startup.
/// </summary>
/// <param name="Files">Number of Markdown files scanned.</param>
/// <param name="Chunks">Total chunks produced from those files.</param>
/// <param name="Embedded">Chunks that were (re-)embedded this run.</param>
/// <param name="Skipped">Chunks left untouched because their content hash was unchanged.</param>
public record IngestionResult(int Files, int Chunks, int Embedded, int Skipped);
