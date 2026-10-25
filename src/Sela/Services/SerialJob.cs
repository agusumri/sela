namespace Sela.Services;

/// <summary>
/// Runs an async job one at a time. UI thread only (no locking).
/// While it runs, a plain call is skipped; a forced or queued call schedules exactly one follow-up run
/// (forced when any of the coalesced callers asked for force) and completes when that run has finished.
/// The job handles and logs its own errors.
/// </summary>
public sealed class SerialJob
{
    private readonly Func<bool, Task> _work;
    private Task? _current;
    private Task? _next;
    private bool _nextForce;

    public SerialJob(Func<bool, Task> work) => _work = work ?? throw new ArgumentNullException(nameof(work));

    public Task Run(bool force = false, bool queue = false)
    {
        if (_current is not { IsCompleted: false } current) return _current = _work(force);
        if (!force && !queue) return Task.CompletedTask;
        _nextForce |= force;
        return _next ??= RunNextAsync(current);
    }

    private async Task RunNextAsync(Task previous)
    {
        try { await previous; }
        catch (Exception) { /* the job logs its own errors */ }
        bool force = _nextForce;
        _nextForce = false;
        _next = null;
        await (_current = _work(force));
    }
}
