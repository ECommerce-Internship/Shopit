using Microsoft.AspNetCore.Http;

namespace Shopit.Application.Interfaces;

public interface IBlobStorageService
{
    Task<string> UploadAsync(IFormFile file, string containerName, string blobName);
    Task DeleteAsync(string blobUrl, string containerName);
}