using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Renci.SshNet;
using Renci.SshNet.Common;
using Shopit.Application.Interfaces;
using Shopit.Application.Products;
using Shopit.Application.Products.DTOs;
using Shopit.Domain.Exceptions;
using Shopit.Infrastructure.Configuration;

namespace Shopit.Infrastructure.Services;

/// <summary>
/// Connects to the configured SFTP server, downloads the product import Excel file
/// and streams it into the existing <see cref="IProductService.ImportAsync"/> pipeline.
/// The Excel parsing/validation is owned entirely by the product import service — this
/// class only handles the SFTP transport.
/// </summary>
public class SftpProductImportService : ISftpProductImportService
{
    private readonly SftpSettings _settings;
    private readonly IProductService _productService;
    private readonly ILogger<SftpProductImportService> _logger;

    public SftpProductImportService(
        IOptions<SftpSettings> settings,
        IProductService productService,
        ILogger<SftpProductImportService> logger)
    {
        _settings = settings.Value;
        _productService = productService;
        _logger = logger;
    }

    public async Task<ImportResultDto> ImportProductsFromSftpAsync(CancellationToken cancellationToken = default)
    {
        using var client = new SftpClient(_settings.Host, _settings.Port, _settings.Username, _settings.Password);

        Connect(client);

        try
        {
            EnsureFileExists(client);

            using var fileStream = new MemoryStream();
            client.DownloadFile(_settings.FilePath, fileStream);
            _logger.LogInformation(
                "Downloaded SFTP file {FilePath} ({Bytes} bytes).",
                _settings.FilePath,
                fileStream.Length);

            // Rewind so the import service reads from the start of the file.
            fileStream.Position = 0;

            var result = await _productService.ImportAsync(fileStream, cancellationToken);
            _logger.LogInformation(
                "SFTP product import completed: {Added} added, {Failed} failed.",
                result.AddedCount,
                result.FailedCount);

            return result;
        }
        finally
        {
            if (client.IsConnected)
                client.Disconnect();
        }
    }

    private void Connect(SftpClient client)
    {
        _logger.LogInformation("Connecting to SFTP server {Host}:{Port}.", _settings.Host, _settings.Port);

        try
        {
            client.Connect();
        }
        catch (Exception ex) when (ex is SshException or SocketException)
        {
            _logger.LogError(ex, "Failed to connect to SFTP server {Host}:{Port}.", _settings.Host, _settings.Port);
            throw new ExternalServiceException(
                $"Could not connect to the SFTP server at {_settings.Host}:{_settings.Port}.");
        }

        _logger.LogInformation("SFTP connection to {Host}:{Port} succeeded.", _settings.Host, _settings.Port);
    }

    private void EnsureFileExists(SftpClient client)
    {
        bool exists;
        try
        {
            exists = client.Exists(_settings.FilePath);
        }
        catch (SftpPathNotFoundException)
        {
            // A missing parent directory surfaces as a path-not-found error.
            exists = false;
        }

        if (!exists)
        {
            _logger.LogWarning("SFTP file {FilePath} was not found.", _settings.FilePath);
            throw new NotFoundException(
                $"The file '{_settings.FilePath}' was not found on the SFTP server.");
        }
    }
}
