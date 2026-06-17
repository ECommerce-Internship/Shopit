using System.Security.Claims;

namespace Shopit.Application.Interfaces;

public interface IExternalAuthService
{
    Task<(string AccessToken, string RefreshToken)> HandleCallbackAsync(string provider, IEnumerable<Claim> claims);
}