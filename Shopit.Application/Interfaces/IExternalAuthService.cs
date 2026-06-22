using System.Security.Claims;
using Shopit.Application.DTOs.Auth;

namespace Shopit.Application.Interfaces;

public interface IExternalAuthService
{
    Task<AuthResponse> HandleCallbackAsync(string provider, IEnumerable<Claim> claims);
}