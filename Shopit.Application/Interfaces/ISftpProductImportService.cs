using Shopit.Application.Products.DTOs;

namespace Shopit.Application.Interfaces;

/// <summary>
/// Downloads the configured Excel file from the SFTP server and feeds it into the
/// existing product import pipeline, returning the same result as a direct upload.
/// </summary>
public interface ISftpProductImportService
{
    Task<ImportResultDto> ImportProductsFromSftpAsync(CancellationToken cancellationToken = default);
}
