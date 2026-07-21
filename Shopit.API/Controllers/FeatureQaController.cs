using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shopit.Application.DTOs.Rag;
using Shopit.Application.Rag;

namespace Shopit.API.Controllers;

/// <summary>
/// Admin operations for the feature-documentation Q&amp;A corpus (SCRUM-166).
/// Answering questions is exposed to users through the chat assistant's
/// <c>answer_feature_question</c> MCP tool, not here — this controller only
/// manages ingestion.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Authorize(Roles = "Admin")]
[Route("api/v{version:apiVersion}/feature-qa")]
public class FeatureQaController : ControllerBase
{
    private readonly IFeatureDocIngestionService _ingestionService;

    public FeatureQaController(IFeatureDocIngestionService ingestionService)
    {
        _ingestionService = ingestionService;
    }

    /// <summary>
    /// Re-ingests the feature documentation: chunks each Markdown file, embeds the
    /// chunks, and stores the vectors. Idempotent — unchanged chunks are not
    /// re-embedded. Run this after editing the docs so subsequent answers reflect
    /// the new content.
    /// </summary>
    [HttpPost("reindex")]
    [ProducesResponseType(typeof(IngestionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<IngestionResult>> Reindex(CancellationToken cancellationToken)
    {
        var result = await _ingestionService.ReindexAsync(cancellationToken);
        return Ok(result);
    }
}
