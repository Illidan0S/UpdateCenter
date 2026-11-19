using UpdateCenter.Models;
using UpdateCenter.Services;

internal static class StatusWatchdogHardeningTests
{
    internal static void Run()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var started = now.AddHours(-2);
        var latest = new UpdateRunStatus
        {
            State = "Running", LastHeartbeatUtc = now.AddSeconds(-3),
            LastProgressUtc = now.AddMinutes(-13), CurrentItemStartedUtc = now.AddMinutes(-20)
        };
        UpdateWatchdogDecision Poll(UpdateRunStatus? observed, DateTime at) =>
            UpdateWatchdogPolicy.EvaluatePoll(null, observed, started, at, UpdateWatchdogThresholds.Default).Decision;

        var missed = Poll(latest, now);
        if (missed.ShouldTerminate || !missed.ShouldWarnProgress)
            throw new InvalidOperationException("A missed poll must supervise the last real heartbeat.");

        if (Poll(null, started.AddSeconds(75)).ShouldTerminate ||
            Poll(null, started.AddSeconds(76)).TerminationReason != "initial-status-timeout")
            throw new InvalidOperationException("A runner that never published status needs a startup deadline.");
        if (Poll(latest, now.AddSeconds(80)).TerminationReason != "runner-heartbeat-timeout")
            throw new InvalidOperationException("A missed poll must not renew the last heartbeat.");

        latest.CurrentItemStartedUtc = now.AddMinutes(-91);
        if (Poll(latest, now).TerminationReason != "absolute-item-timeout")
            throw new InvalidOperationException("Missed reads must preserve the absolute item timeout.");
    }
}
