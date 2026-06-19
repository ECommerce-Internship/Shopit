namespace Shopit.Domain.Exceptions;

/// <summary>
/// Thrown when an external/upstream service (e.g. the Gemini API) fails or returns
/// an unusable response. Mapped to HTTP 502 Bad Gateway.
/// </summary>
public class ExternalServiceException : Exception
{
    public ExternalServiceException(string message) : base(message) { }
}
