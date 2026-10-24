using System.IO;
using Sela.Core;
using Sela.Localization;
using Forms = System.Windows.Forms;

namespace Sela.Services;

/// <summary>Notification-area icon with the localized context menu (texts follow <see cref="Loc.LanguageChanged"/>).</summary>
public sealed class TrayIcon : IDisposable
{
    private const int MaxTooltip = 127; // NotifyIcon.Text limit
    private readonly Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon? _ownedIcon;
    private readonly List<(Forms.ToolStripItem Item, string Key)> _texts = new();
    private (Prayer Prayer, DateTime Time)? _next;

    public TrayIcon(IAppCommands commands, Action showPanel, Action showHadith, Action resetPlacement,
        Action testNotify, Action exit, Func<bool> isMuted, Action<bool> setMuted, Func<bool> isAdhanPlaying)
    {
        _ownedIcon = LoadIcon();
        var menu = new Forms.ContextMenuStrip();
        Add(menu, "Shell_TraySchedule", showPanel);
        Add(menu, "Shell_TrayHadith", showHadith);
        Add(menu, "Shell_TrayResetNotch", resetPlacement);
        Add(menu, "Shell_TraySettings", commands.OpenSettings);
        Add(menu, "Shell_TrayTestNotify", testNotify);
        var mute = new Forms.ToolStripMenuItem { CheckOnClick = true };
        mute.Click += (_, _) => setMuted(mute.Checked);
        Track(menu, mute, "Shell_TrayMuteAdhan");
        var stop = new Forms.ToolStripMenuItem(string.Empty, null, (_, _) => commands.StopAdhan());
        Track(menu, stop, "Shell_TrayStopAdhan");
        menu.Opening += (_, _) =>
        {
            mute.Checked = isMuted();
            stop.Enabled = isAdhanPlaying();
        };
        Add(menu, "Shell_TrayRefresh", commands.RefreshNow);
        menu.Items.Add(new Forms.ToolStripSeparator());
        Add(menu, "Shell_TrayQuit", exit);

        _icon = new Forms.NotifyIcon
        {
            Icon = _ownedIcon ?? System.Drawing.SystemIcons.Application,
            Text = "Sela",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => commands.OpenSettings();
        ApplyTexts();
        Loc.Instance.LanguageChanged += ApplyTexts;
    }

    private void Add(Forms.ContextMenuStrip menu, string key, Action click) =>
        Track(menu, new Forms.ToolStripMenuItem(string.Empty, null, (_, _) => click()), key);

    private void Track(Forms.ContextMenuStrip menu, Forms.ToolStripItem item, string key)
    {
        menu.Items.Add(item);
        _texts.Add((item, key));
    }

    private void ApplyTexts()
    {
        foreach (var (item, key) in _texts) item.Text = Loc.T(key);
        SetNextPrayer(_next);
    }

    public void SetNextPrayer((Prayer Prayer, DateTime Time)? next)
    {
        _next = next;
        var text = next is { } n
            ? Loc.F("Shell_TrayTooltip", Names.Prayer(n.Prayer, Loc.Instance.Language), n.Time.ToString("HH:mm"))
            : "Sela";
        if (text.Length > MaxTooltip) text = text[..MaxTooltip];
        if (_icon.Text != text) _icon.Text = text;
    }

    private static System.Drawing.Icon? LoadIcon()
    {
        try
        {
            var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Sela.ico"));
            if (info is null) return null;
            using var stream = info.Stream;
            return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UriFormatException)
        {
            return null; // icon asset not present yet
        }
    }

    public void Dispose()
    {
        Loc.Instance.LanguageChanged -= ApplyTexts;
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _ownedIcon?.Dispose();
    }
}
