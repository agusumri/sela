using System.IO;
using Sela.Core;
using Forms = System.Windows.Forms;

namespace Sela.Services;

/// <summary>Notification-area icon with the Indonesian context menu.</summary>
public sealed class TrayIcon : IDisposable
{
    private const int MaxTooltip = 127; // NotifyIcon.Text limit
    private readonly Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon? _ownedIcon;

    public TrayIcon(IAppCommands commands, Action showPanel, Action testNotify, Action exit,
        Func<bool> isMuted, Action<bool> setMuted, Func<bool> isAdhanPlaying)
    {
        _ownedIcon = LoadIcon();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Jadwal & agenda", null, (_, _) => showPanel());
        menu.Items.Add("Pengaturan", null, (_, _) => commands.OpenSettings());
        menu.Items.Add("Tes notifikasi", null, (_, _) => testNotify());
        var mute = new Forms.ToolStripMenuItem("Bisukan adzan") { CheckOnClick = true };
        mute.Click += (_, _) => setMuted(mute.Checked);
        menu.Items.Add(mute);
        var stop = new Forms.ToolStripMenuItem("Hentikan adzan", null, (_, _) => commands.StopAdhan());
        menu.Items.Add(stop);
        menu.Opening += (_, _) =>
        {
            mute.Checked = isMuted();
            stop.Enabled = isAdhanPlaying();
        };
        menu.Items.Add("Segarkan", null, (_, _) => commands.RefreshNow());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Keluar", null, (_, _) => exit());

        _icon = new Forms.NotifyIcon
        {
            Icon = _ownedIcon ?? System.Drawing.SystemIcons.Application,
            Text = "Sela",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => commands.OpenSettings();
    }

    public void SetNextPrayer((Prayer Prayer, DateTime Time)? next)
    {
        var text = next is { } n ? $"Sela — {ReminderScheduler.Name(n.Prayer)} {n.Time:HH:mm}" : "Sela";
        _icon.Text = text.Length > MaxTooltip ? text[..MaxTooltip] : text;
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
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _ownedIcon?.Dispose();
    }
}
