using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Sela.Core;

namespace Sela.Services;

/// <summary>
/// Read-only access to classic Outlook's default calendar via COM late binding.
/// Only attaches to an Outlook that is already running; never launches it. All COM work runs on a
/// dedicated STA thread; a call is awaited for at most 20 s, and while a previous call is still stuck no new
/// thread is started (the last good result is returned). Never throws for "unavailable": returns empty.
/// </summary>
public sealed class OutlookCalendarProvider : IRefreshableCalendarProvider
{
    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(20);
    private const int OlFolderCalendar = 9;
    private const int OlResponseDeclined = 4;
    private const int OlMeetingCanceled = 5;
    private const int OlMeetingReceivedAndCanceled = 7;
    private const int MaxItems = 500;
    private const int MaxReminderMinutes = 7 * 24 * 60;

    // Static: App rebuilds the provider on every settings save, but a hung COM thread outlives the instance.
    private static readonly object Gate = new();
    private static Task<IReadOnlyList<CalendarEvent>>? s_inFlight;
    private static IReadOnlyList<CalendarEvent> s_lastResult = Array.Empty<CalendarEvent>();
    private static bool s_hangLogged;

    private readonly Func<int> _defaultReminderMinutes;
    private readonly Action<string>? _log;
    private readonly bool _forceUnavailable;
    private volatile bool _lastFetchFailed;

    /// <param name="forceUnavailable">Debug (--offline): behave as if Outlook were not running.</param>
    public OutlookCalendarProvider(Func<int> defaultReminderMinutes, Action<string>? log = null, bool forceUnavailable = false)
    {
        _defaultReminderMinutes = defaultReminderMinutes ?? throw new ArgumentNullException(nameof(defaultReminderMinutes));
        _log = log;
        _forceUnavailable = forceUnavailable;
    }

    public CalendarSource Source => CalendarSource.Outlook;

    /// <summary>True when Outlook isn't running, the COM call failed, timed out or is still stuck.</summary>
    public bool LastFetchFailed => _lastFetchFailed;

    /// <summary>No-op: Outlook is read live on every call.</summary>
    public void RequestFreshFetch() { }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        _lastFetchFailed = true; // until a read completes
        if (_forceUnavailable || !IsOutlookRunning())
        {
            lock (Gate) s_lastResult = Array.Empty<CalendarEvent>();
            return Array.Empty<CalendarEvent>();
        }

        Task<IReadOnlyList<CalendarEvent>> call;
        lock (Gate)
        {
            // A hung Outlook (modal dialog, object-model guard prompt) blocks the STA thread indefinitely.
            // Never stack another thread on top of it; serve the last good result until it returns.
            if (s_inFlight is { IsCompleted: false })
            {
                if (!s_hangLogged)
                {
                    s_hangLogged = true;
                    _log?.Invoke("Outlook tidak merespons, dilewati");
                }
                return s_lastResult;
            }
            call = s_inFlight = StartStaCall(from, to, _defaultReminderMinutes());
            s_hangLogged = false;
        }

        try
        {
            var events = await call.WaitAsync(CallTimeout, ct).ConfigureAwait(false);
            _lastFetchFailed = false;
            return events;
        }
        catch (OutlookUnavailableException)
        {
            return Array.Empty<CalendarEvent>(); // process found but not attachable (starting up / closing)
        }
        catch (TimeoutException)
        {
            _log?.Invoke("Outlook: timeout");
            lock (Gate) return s_lastResult;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log?.Invoke($"Outlook: {ex.GetType().Name}: {ex.Message}");
            return Array.Empty<CalendarEvent>();
        }
    }

    private Task<IReadOnlyList<CalendarEvent>> StartStaCall(DateTime from, DateTime to, int defaultReminder)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<CalendarEvent>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var result = Read(from, to, defaultReminder);
                lock (Gate) s_lastResult = result; // also lands when a timed-out call finally completes
                tcs.TrySetResult(result);
            }
            catch (Exception ex) { tcs.TrySetException(ex); }
        })
        {
            IsBackground = true, // an Outlook that hangs must not keep the process alive
            Name = "Sela Outlook COM",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    private static bool IsOutlookRunning()
    {
        var processes = Process.GetProcessesByName("OUTLOOK");
        foreach (var p in processes) p.Dispose();
        return processes.Length > 0;
    }

    private IReadOnlyList<CalendarEvent> Read(DateTime from, DateTime to, int defaultReminder)
    {
        object? app = TryGetRunningOutlook();
        if (app is null) throw new OutlookUnavailableException();

        dynamic? ns = null, folder = null, items = null, restricted = null;
        var result = new List<CalendarEvent>();
        try
        {
            ns = ((dynamic)app).GetNamespace("MAPI");
            folder = ns.GetDefaultFolder(OlFolderCalendar);
            items = folder.Items;
            items.Sort("[Start]");
            items.IncludeRecurrences = true;

            // Outlook parses Restrict dates in the Windows locale, so format with the current culture.
            var culture = CultureInfo.CurrentCulture;
            string filter = $"[Start] >= '{from.ToString("g", culture)}' AND [Start] < '{to.ToString("g", culture)}'";
            restricted = items.Restrict(filter);

            int count = 0;
            foreach (dynamic item in restricted)
            {
                try
                {
                    if (++count > MaxItems) break;
                    var e = ToEvent(item, defaultReminder);
                    if (e is not null) result.Add(e);
                }
                catch (Exception ex)
                {
                    // Non-appointment items (or ones Outlook refuses to expose) are skipped.
                    _log?.Invoke($"Outlook: item dilewati ({ex.GetType().Name})");
                }
                finally
                {
                    Release(item);
                }
            }
        }
        finally
        {
            Release(restricted, final: true);
            Release(items, final: true);
            Release(folder, final: true);
            Release(ns, final: true);
            Release(app);
        }

        result.Sort((a, b) => a.Start.CompareTo(b.Start));
        return result;
    }

    private static CalendarEvent? ToEvent(dynamic item, int defaultReminder)
    {
        if ((bool)item.AllDayEvent) return null;
        if ((int)item.ResponseStatus == OlResponseDeclined) return null;
        int meetingStatus = (int)item.MeetingStatus;
        if (meetingStatus is OlMeetingCanceled or OlMeetingReceivedAndCanceled) return null;

        DateTime start = (DateTime)item.Start;
        DateTime end = (DateTime)item.End;
        string subject = (string?)item.Subject is { Length: > 0 } s ? s.Trim() : "(Tanpa judul)";
        string? location = (string?)item.Location is { Length: > 0 } l ? l.Trim() : null;
        int reminder = (bool)item.ReminderSet ? (int)item.ReminderMinutesBeforeStart : defaultReminder;

        string? body = null;
        try { body = (string?)item.Body; }
        catch (Exception) { /* Body can be blocked by the Outlook object-model guard; location may still have the link. */ }

        string key = $"{(string)item.EntryID}|{start.ToString("s", CultureInfo.InvariantCulture)}";
        return new CalendarEvent(
            key, subject,
            DateTime.SpecifyKind(start, DateTimeKind.Unspecified),
            DateTime.SpecifyKind(end < start ? start : end, DateTimeKind.Unspecified),
            location,
            JoinLinkExtractor.Find(location, body),
            Math.Clamp(reminder, 0, MaxReminderMinutes),
            CalendarSource.Outlook);
    }

    private static object? TryGetRunningOutlook()
    {
        try
        {
            CLSIDFromProgID("Outlook.Application", out var clsid);
            GetActiveObject(ref clsid, IntPtr.Zero, out var obj);
            return obj;
        }
        catch (Exception)
        {
            return null; // not registered or not running (MK_E_UNAVAILABLE)
        }
    }

    private static void Release(object? com, bool final = false)
    {
        if (com is null || !Marshal.IsComObject(com)) return;
        try
        {
            if (final) Marshal.FinalReleaseComObject(com);
            else Marshal.ReleaseComObject(com);
        }
        catch (Exception) { }
    }

    private sealed class OutlookUnavailableException : Exception;

    [DllImport("ole32.dll", PreserveSig = false)]
    private static extern void CLSIDFromProgID([MarshalAs(UnmanagedType.LPWStr)] string progId, out Guid clsid);

    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(ref Guid clsid, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object obj);
}
