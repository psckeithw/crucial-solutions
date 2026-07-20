namespace ServiceNowToAdo.Services;

public interface IIdempotencyLockService
{
    Task<IAsyncDisposable?> TryAcquireAsync(string org, string incidentNumber, CancellationToken ct);
}

public sealed class DuplicateSentinel : IAsyncDisposable
{
    public DuplicateSentinel(ExistingWorkItem existing)
    {
        Existing = existing;
    }

    public ExistingWorkItem Existing { get; }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
