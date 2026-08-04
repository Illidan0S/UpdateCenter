namespace UpdateCenter.Core;

public sealed class SingleOperationGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private int _disposed;

    public bool IsBusy => _semaphore.CurrentCount == 0;

    public async Task<IDisposable?> TryEnterAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!await _semaphore.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
            return null;
        return new Lease(_semaphore);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _semaphore.Dispose();
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable
    {
        private SemaphoreSlim? _semaphore = semaphore;
        public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
    }
}
