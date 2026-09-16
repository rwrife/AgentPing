using System.Text;
using System.Text.RegularExpressions;

namespace AgentPing.Companion.Core;

public sealed record DesktopNotice(string Key, string AppId, string AppName, string Text);

public sealed class NotificationForwardingPolicy
{
    private HashSet<string>? _seen;
    public void Reset() => _seen = null;

    // Baseline existing notifications on startup. Mark even disallowed notices
    // as seen so checking an app does not replay its notification history.
    public IReadOnlyList<DesktopNotice> TakeNew(IReadOnlyList<DesktopNotice> snapshot, ISet<string> allowed)
    {
        var current = snapshot.Select(n => n.Key).ToHashSet(StringComparer.Ordinal);
        var result = _seen is null ? [] : snapshot
            .Where(n => !_seen.Contains(n.Key) && allowed.Contains(n.AppId))
            .DistinctBy(n => n.Key).ToArray();
        _seen = current;
        return result;
    }

    public static string RobotText(string appName, string text)
    {
        var source = $"{appName}: {text}".Replace("…", "...").Replace('’', '\'')
            .Replace('“', '"').Replace('”', '"').Replace('—', '-').Replace('–', '-');
        var output = new StringBuilder();
        foreach (var rune in source.Normalize(NormalizationForm.FormD).EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            output.Append(rune.Value is >= 32 and <= 126 ? (char)rune.Value : ' ');
        }
        var clean = Regex.Replace(output.ToString(), @"\s+", " ").Trim();
        return clean.Length <= 192 ? clean : clean[..189] + "...";
    }
}
