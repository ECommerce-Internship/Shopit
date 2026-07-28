using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Http;
using Shopit.Application.Interfaces;

namespace Shopit.Infrastructure.Services;

/// <summary>
/// Cloudflare R2 implementation of IBlobStorageService.
/// R2 is S3-compatible so we use the AWS S3 SDK pointed at the R2 endpoint.
/// </summary>
public class R2BlobStorageService : IBlobStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly string _publicUrl;

    public R2BlobStorageService(IAmazonS3 s3Client, string bucketName, string publicUrl)
    {
        _s3Client = s3Client;
        _bucketName = bucketName;
        _publicUrl = publicUrl.TrimEnd('/');
    }

    public async Task<string> UploadAsync(IFormFile file, string containerName, string blobName)
    {
        var key = $"{containerName}/{blobName}";

        using var stream = file.OpenReadStream();

        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = stream,
            ContentType = file.ContentType,
            DisablePayloadSigning = true
        };

        await _s3Client.PutObjectAsync(request);

        return $"{_publicUrl}/{key}";
    }

    public async Task DeleteAsync(string blobUrl, string containerName)
    {
        var uri = new Uri(blobUrl);
        var key = uri.AbsolutePath.TrimStart('/');

        var request = new DeleteObjectRequest
        {
            BucketName = _bucketName,
            Key = key
        };

        await _s3Client.DeleteObjectAsync(request);
    }
}