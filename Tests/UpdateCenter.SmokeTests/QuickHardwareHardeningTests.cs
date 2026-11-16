using UpdateCenter.Services;

internal static class QuickHardwareHardeningTests
{
    internal static async Task RunAsync()
    {
        using var caller = new CancellationTokenSource();
        var result = QuickHardwareDataService.CaptureAsync<string>(token =>
        {
            caller.Cancel();
            return Task.FromCanceled<string>(token);
        }, "fixture", caller.Token);
        try
        {
            await result;
            throw new InvalidOperationException("Quick hardware swallowed external cancellation.");
        }
        catch (OperationCanceledException) when (caller.IsCancellationRequested) { }

        var timeout = await QuickHardwareDataService.CaptureAsync<string>(
            _ => Task.FromCanceled<string>(new CancellationToken(true)), "fixture", CancellationToken.None);
        var unavailable = await QuickHardwareDataService.CaptureAsync<string>(
            _ => throw new IOException("Fixture unavailable"), "fixture", CancellationToken.None);
        if (timeout is not null || unavailable is not null)
            throw new InvalidOperationException("Partial inventory should survive a local timeout or unavailable subsystem.");
    }
}
