using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ServiceNowToAdo.Services;

public sealed class IdempotencyLockService : IIdempotencyLockService
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(65);

    private readonly IBlobLeaseStore _blobStore;
    private readonly IAdoClient _ado;
    private readonly ILogger<IdempotencyLockService> _log;

    public IdempotencyLockService(
        IOptions<BlobStorageOptions> blobOptions,
        IAdoClient ado,
        ILogger<IdempotencyLockService> log)
        : this(new AzureBlobLeaseStore(blobOptions.Value), ado, log)
    {
    }

    public IdempotencyLockService(
        IBlobLeaseStore blobStore,
        IAdoClient ado,
        ILogger<IdempotencyLockService> log)
    {
        _blobStore = blobStore;
        _ado = ado;
        _log = log;
    }

    public async Task<IAsyncDisposable?> TryAcquireAsync(string org, string incidentNumber, CancellationToken ct)
    {
        var blobName = $"{org}/{incidentNumber}";
        await _blobStore.EnsureBlobExistsAsync(blobName, ct);

        var leaseId = await _blobStore.TryAcquireLeaseAsync(blobName, LeaseDuration, ct);
        if (!string.IsNullOrEmpty(leaseId))
        {
            return new LeaseHandle(_blobStore, blobName, leaseId);
        }

        _log.LogInformation("Outcome=lock-contention Scope={Scope} Incident={Incident}", org, incidentNumber);
        var deadline = DateTimeOffset.UtcNow + MaxWait;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var existing = await _ado.FindByIncidentNumberAsync(org, incidentNumber, ct);
            if (existing is not null)
            {
                return new DuplicateSentinel(existing);
            }

            await Task.Delay(PollInterval, ct);

            leaseId = await _blobStore.TryAcquireLeaseAsync(blobName, LeaseDuration, ct);
            if (!string.IsNullOrEmpty(leaseId))
            {
                return new LeaseHandle(_blobStore, blobName, leaseId);
            }
        }

        var finalExisting = await _ado.FindByIncidentNumberAsync(org, incidentNumber, ct);
        if (finalExisting is not null)
        {
            return new DuplicateSentinel(finalExisting);
        }

        throw new InvalidOperationException($"Timed out waiting for idempotency lock for incident '{incidentNumber}'.");
    }
}

public interface IBlobLeaseStore
{
    Task EnsureBlobExistsAsync(string blobName, CancellationToken ct);
    Task<string?> TryAcquireLeaseAsync(string blobName, TimeSpan duration, CancellationToken ct);
    Task ReleaseLeaseAsync(string blobName, string leaseId, CancellationToken ct);
}

internal sealed class AzureBlobLeaseStore : IBlobLeaseStore
{
    private readonly BlobContainerClient _container;
    private readonly Task _ensureContainerTask;

    public AzureBlobLeaseStore(BlobStorageOptions options)
    {
        var service = new BlobServiceClient(options.ConnectionString);
        _container = service.GetBlobContainerClient(options.ContainerName);
        _ensureContainerTask = _container.CreateIfNotExistsAsync();
    }

    public async Task EnsureBlobExistsAsync(string blobName, CancellationToken ct)
    {
        await _ensureContainerTask.WaitAsync(ct);
        var blob = _container.GetBlobClient(blobName);

        try
        {
            await blob.UploadAsync(BinaryData.FromBytes([]), overwrite: false, cancellationToken: ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            // blob already exists
        }
    }

    public async Task<string?> TryAcquireLeaseAsync(string blobName, TimeSpan duration, CancellationToken ct)
    {
        await _ensureContainerTask.WaitAsync(ct);
        var blob = _container.GetBlobClient(blobName);
        var leaseClient = blob.GetBlobLeaseClient();
        try
        {
            var response = await leaseClient.AcquireAsync(duration, cancellationToken: ct);
            return response.Value.LeaseId;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            return null;
        }
    }

    public async Task ReleaseLeaseAsync(string blobName, string leaseId, CancellationToken ct)
    {
        await _ensureContainerTask.WaitAsync(ct);
        var blob = _container.GetBlobClient(blobName);
        var leaseClient = blob.GetBlobLeaseClient(leaseId);
        await leaseClient.ReleaseAsync(cancellationToken: ct);
    }
}

internal sealed class LeaseHandle : IAsyncDisposable
{
    private readonly IBlobLeaseStore _blobStore;
    private readonly string _blobName;
    private readonly string _leaseId;
    private int _disposed;

    public LeaseHandle(IBlobLeaseStore blobStore, string blobName, string leaseId)
    {
        _blobStore = blobStore;
        _blobName = blobName;
        _leaseId = leaseId;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _blobStore.ReleaseLeaseAsync(_blobName, _leaseId, CancellationToken.None);
    }
}
