using Shopit.Domain.Entities;

namespace Shopit.Application.Interfaces;

public interface IJwtTokenService
{
    string GenerateAccessToken(User user, IEnumerable<int> storeIds);
}