using System.Globalization;
using System.Text.Json;

namespace Sela.Core;

/// <summary>Remembers which habits were done on which day (habits.json, last 60 days).</summary>
public sealed class HabitTracker
{
    private const int KeepDays = 60;
    private const string DayFormat = "yyyy-MM-dd";

    private readonly string? _path;
    private Dictionary<Habit, HashSet<DateOnly>>? _done;

    /// <param name="path">JSON file to persist to; null keeps everything in memory (used by --mock).</param>
    public HabitTracker(string? path) { _path = path; }
    public static HabitTracker Default() => new(DefaultPath);
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sela", "habits.json");

    public bool IsDone(Habit habit, DateOnly day) => Load().TryGetValue(habit, out var days) && days.Contains(day);

    public void MarkDone(Habit habit, DateOnly day)
    {
        var done = Load();
        if (!done.TryGetValue(habit, out var days)) done[habit] = days = new HashSet<DateOnly>();
        days.Add(day);

        // Keep only the last 60 days, counted back from the day just marked.
        var cutoff = day.AddDays(-(KeepDays - 1));
        foreach (var set in done.Values) set.RemoveWhere(d => d < cutoff);
        Save(done);
    }

    private Dictionary<Habit, HashSet<DateOnly>> Load()
    {
        if (_done is not null) return _done;
        _done = new Dictionary<Habit, HashSet<DateOnly>>();
        if (_path is null) return _done;
        try
        {
            if (!File.Exists(_path)) return _done;
            var raw = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(_path));
            if (raw is null) return _done;
            foreach (var (name, list) in raw)
            {
                if (!Enum.TryParse<Habit>(name, out var habit) || !Enum.IsDefined(habit) || list is null) continue;
                var set = new HashSet<DateOnly>();
                foreach (var s in list)
                    if (DateOnly.TryParseExact(s, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                        set.Add(d);
                _done[habit] = set;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _done.Clear(); // corrupt/unreadable: start empty
        }
        return _done;
    }

    private void Save(Dictionary<Habit, HashSet<DateOnly>> done)
    {
        if (_path is null) return;
        var raw = done.Where(kv => kv.Value.Count > 0).ToDictionary(
            kv => kv.Key.ToString(),
            kv => kv.Value.OrderBy(d => d).Select(d => d.ToString(DayFormat, CultureInfo.InvariantCulture)).ToList());
        try { SafeIo.WriteAllTextAtomic(_path, JsonSerializer.Serialize(raw)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* keep in memory; retry on next save */ }
    }
}

/// <summary>
/// Decides when to ask "sudah dzikir / sholawat?". Windows come from today's schedule and suit a work PC:
/// DzikirPagi Subuh+15m .. Dzuhur, Sholawat Dzuhur+30m .. Ashar, DzikirPetang Ashar+15m .. Maghrib.
/// Each enabled habit fires once per window (the first tick inside it), again after a 30 min snooze,
/// and never once marked done for the day.
/// </summary>
public sealed class HabitScheduler
{
    public static readonly TimeSpan SnoozeLength = TimeSpan.FromMinutes(30);

    /// <summary>Per-habit state for the current day. NextAt null = shown, waiting for an answer or a snooze.</summary>
    private sealed class State
    {
        public int Count;
        public DateTime? NextAt;
        public DateTime WindowEnd;
    }

    private static readonly Habit[] All = Enum.GetValues<Habit>();
    private readonly Dictionary<Habit, State> _state = new();
    private DateOnly _day;

    public IReadOnlyList<Reminder> Due(DateTime now, DaySchedule today, SelaSettings settings, HabitTracker tracker)
    {
        var day = today.Date;
        if (day != _day) { _state.Clear(); _day = day; }

        var due = new List<Reminder>();
        foreach (var habit in All)
        {
            if (!settings.IsHabitOn(habit) || tracker.IsDone(habit, day)) continue;
            var (start, end) = Window(habit, today);
            if (now < start || now >= end) continue;

            if (!_state.TryGetValue(habit, out var st))
                _state[habit] = st = new State { NextAt = start, WindowEnd = end };
            if (st.NextAt is not { } at || now < at) continue;

            due.Add(Build(habit, day, st.Count, at));
            st.NextAt = null;
        }
        return due;
    }

    /// <summary>"Nanti": ask again after <see cref="SnoozeLength"/>, if that is still inside the window.</summary>
    public void Snooze(Habit habit, DateTime now)
    {
        if (!_state.TryGetValue(habit, out var st) || now >= st.WindowEnd) return;
        st.Count++;
        st.NextAt = now + SnoozeLength;
    }

    public static string Name(Habit habit) => habit switch
    {
        Habit.DzikirPagi => "Dzikir pagi",
        Habit.DzikirPetang => "Dzikir petang",
        _ => "Sholawat",
    };

    private static (DateTime Start, DateTime End) Window(Habit habit, DaySchedule s) => habit switch
    {
        Habit.DzikirPagi => (s[Prayer.Subuh].AddMinutes(15), s[Prayer.Dzuhur]),
        Habit.Sholawat => (s[Prayer.Dzuhur].AddMinutes(30), s[Prayer.Ashar]),
        _ => (s[Prayer.Ashar].AddMinutes(15), s[Prayer.Maghrib]),
    };

    private static Reminder Build(Habit habit, DateOnly day, int n, DateTime dueAt)
    {
        var (title, body) = habit switch
        {
            Habit.DzikirPagi => ("Sudah dzikir pagi?", "Luangkan sebentar untuk dzikir pagi."),
            Habit.DzikirPetang => ("Sudah dzikir petang?", "Sempatkan dzikir petang sebelum Maghrib."),
            _ => ("Sudah bersholawat hari ini?", "Allahumma shalli 'ala Muhammad."),
        };
        var key = $"habit|{day.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}|{habit}|{n}";
        return new Reminder(key, ReminderKind.Habit, title, body, null, null, dueAt, Habit: habit);
    }
}
