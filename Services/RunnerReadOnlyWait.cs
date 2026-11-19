namespace UpdateCenter.Services;

// Only read-only work belongs here. An uncooperative provider may finish late;
// this task must NEVER contain an installer launch or other mutating effects.
internal static class RunnerReadOnlyWait
{
    internal static async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> read,
        CancellationToken token, TimeSpan timeout, string phase)
    {
        token.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        var work = Task.Run(() => read(deadline.Token), deadline.Token);
        // Observe a late fault even when an uncooperative provider ignores cancellation.
        _ = work.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        try
        {
            var result = await work.WaitAsync(deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new TimeoutException(LocalizationService.Text(
                $"Tempo massimo superato nella fase {phase}. Nessun nuovo avvio autorizzato; ripeti scansione e riepilogo.",
                $"Time limit exceeded during {phase}. No new launch authorized; repeat the scan and summary."));
        }
    }
}
