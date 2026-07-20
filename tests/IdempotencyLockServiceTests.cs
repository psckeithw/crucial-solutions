using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceNowToAdo.Services;

namespace ServiceNowToAdo.Tests;

public class IdempotencyLockServiceTests
{
    [Fact]
    public async Task TryAcquireAsync_LockAcquired_ReturnsDisposableLease()
    {
        var blobStore = new FakeBlobLeaseStore(["lease-1"]);
        var ado = new StubAdoClient();
        var service = new IdempotencyLockService(blobStore, ado, NullLogger<IdempotencyLockService>.Instance);

        var lease = await service.TryAcquireAsync("myorg", "INC001", CancellationToken.None);

        Assert.NotNull(lease);
        Assert.IsNotType<DuplicateSentinel>(lease);
    }

    [Fact]
    public async Task TryAcquireAsync_LockHeld409AndWinnerCreated_ReturnsDuplicateSentinel()
    {
        var blobStore = new FakeBlobLeaseStore([null]);
        var existing = new ExistingWorkItem(123, "https://dev.azure.com/myorg/UPO/_workitems/edit/123", "UPO");
        var ado = new StubAdoClient { ExistingItem = existing };
        var service = new IdempotencyLockService(blobStore, ado, NullLogger<IdempotencyLockService>.Instance);

        var lease = await service.TryAcquireAsync("myorg", "INC001", CancellationToken.None);

        var duplicate = Assert.IsType<DuplicateSentinel>(lease);
        Assert.Equal(123, duplicate.Existing.Id);
    }

    [Fact]
    public async Task LeaseHandle_Dispose_ReleasesBlobLease()
    {
        var blobStore = new FakeBlobLeaseStore(["lease-42"]);
        var ado = new StubAdoClient();
        var service = new IdempotencyLockService(blobStore, ado, NullLogger<IdempotencyLockService>.Instance);

        var lease = await service.TryAcquireAsync("myorg", "INC002", CancellationToken.None);
        Assert.NotNull(lease);

        await lease!.DisposeAsync();

        Assert.Equal(1, blobStore.ReleaseCalls);
        Assert.Equal("myorg/INC002", blobStore.ReleasedBlobName);
        Assert.Equal("lease-42", blobStore.ReleasedLeaseId);
    }
}

internal sealed class FakeBlobLeaseStore : IBlobLeaseStore
{
    private readonly Queue<string?> _acquireResults;

    public FakeBlobLeaseStore(IEnumerable<string?> acquireResults)
    {
        _acquireResults = new Queue<string?>(acquireResults);
    }

    public int ReleaseCalls { get; private set; }
    public string? ReleasedBlobName { get; private set; }
    public string? ReleasedLeaseId { get; private set; }

    public Task EnsureBlobExistsAsync(string blobName, CancellationToken ct)
        => Task.CompletedTask;

    public Task<string?> TryAcquireLeaseAsync(string blobName, TimeSpan duration, CancellationToken ct)
    {
        if (_acquireResults.Count == 0)
        {
            return Task.FromResult<string?>(null);
        }

        return Task.FromResult(_acquireResults.Dequeue());
    }

    public Task ReleaseLeaseAsync(string blobName, string leaseId, CancellationToken ct)
    {
        ReleaseCalls++;
        ReleasedBlobName = blobName;
        ReleasedLeaseId = leaseId;
        return Task.CompletedTask;
    }
}
