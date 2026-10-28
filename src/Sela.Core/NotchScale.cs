namespace Sela.Core;

/// <summary>Size of the idle notch pill, in percent of its full size.</summary>
public static class NotchScale
{
    public const int Default = 100;

    /// <summary>The sizes offered in Settings.</summary>
    public static IReadOnlyList<int> Allowed { get; } = new[] { 50, 75, 100 };

    /// <summary>50, 75 or 100; anything else (hand-edited or older settings files) falls back to 100.</summary>
    public static int Normalize(int percent) => percent is 50 or 75 or 100 ? percent : Default;
}
