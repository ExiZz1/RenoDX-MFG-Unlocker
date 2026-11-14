namespace RenoDXMfgUnlocker;

internal enum ReShadeSetupPhase { Idle, Preparing, Watching, AwaitingConfirmation, Rescanning }

internal sealed class ReShadeSetupSession
{
    internal ReShadeSetupPhase Phase { get; private set; }
    internal bool BlocksChanges => Phase != ReShadeSetupPhase.Idle;
    internal bool CanConfirm => Phase == ReShadeSetupPhase.AwaitingConfirmation;

    internal async Task<bool> WaitForQuietExitAsync(Func<Task<bool>> running, Func<Task> interval)
    {
        // Two independent checks help cover the elevated handoff. The interval itself never grants permission.
        while (CanConfirm)
        {
            var first = await running();
            await interval();
            if (CanConfirm && !first && !await running()) return true;
        }
        return false;
    }

    internal async Task<int?> LaunchAsync(Func<Action, Task<int?>> launch, Action changed)
    {
        if (BlocksChanges) throw new InvalidOperationException("Official setup is already running.");
        Phase = ReShadeSetupPhase.Preparing;
        bool started = false;
        try
        {
            changed();
            return await launch(() =>
            {
                started = true;
                Phase = ReShadeSetupPhase.Watching;
                changed();
            });
        }
        finally
        {
            Phase = started ? ReShadeSetupPhase.AwaitingConfirmation : ReShadeSetupPhase.Idle;
            changed();
        }
    }

    internal async Task<bool> FinishAsync(Func<bool> mayStillBeRunning, Func<Task> rescan, Action changed)
    {
        if (!CanConfirm || mayStillBeRunning()) return false;
        Phase = ReShadeSetupPhase.Rescanning;
        try
        {
            changed();
            await rescan();
            return true;
        }
        finally
        {
            Phase = ReShadeSetupPhase.Idle;
            changed();
        }
    }
}
