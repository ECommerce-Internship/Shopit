namespace Shopit.Infrastructure.Configuration;

/// <summary>
/// Strongly typed settings for connecting to the SFTP server that hosts the
/// product import Excel file. Bound from the "Sftp" configuration section.
/// Credentials are supplied via configuration and must never be hardcoded.
/// </summary>
public class SftpSettings
{
    public const string SectionName = "Sftp";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 22;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string FilePath { get; set; } = "/upload/products.xlsx";
}
