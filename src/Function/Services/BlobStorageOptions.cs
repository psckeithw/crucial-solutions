using System.ComponentModel.DataAnnotations;

namespace ServiceNowToAdo.Services;

public sealed class BlobStorageOptions
{
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    public string ContainerName { get; set; } = "snow-ado-locks";
}
