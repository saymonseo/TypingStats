using System.Globalization;

namespace TypingStats.Core;

public sealed class Counters
{
    public long Observed { get; set; }
    public long Estimated { get; set; }
    public long LinkedBackspaces { get; set; }
    public long PrimaryKeys { get; set; }
    public long Repeats { get; set; }
    public long Backspaces { get; set; }
    public long Deletes { get; set; }
    public long WordDeletes { get; set; }
    public long Pastes { get; set; }
    public long Undo { get; set; }
    public long Redo { get; set; }
    public long Cuts { get; set; }
    public long Unresolved { get; set; }
    public long Lost { get; set; }
    public long Commits { get; set; }
    public long Cancels { get; set; }
    public long ActiveMs { get; set; }
    public long Whitespace { get; set; }
    public long Injected { get; set; }
    public long KeyPresses { get; set; }
    public long KeyActiveMs { get; set; }
    public long Gross => Observed + Estimated;
    public long Net => Gross - LinkedBackspaces;
    public Counters Copy() => (Counters)MemberwiseClone();
    public void Add(Counters other)
    {
        Observed += other.Observed; Estimated += other.Estimated; LinkedBackspaces += other.LinkedBackspaces;
        PrimaryKeys += other.PrimaryKeys; Repeats += other.Repeats; Backspaces += other.Backspaces;
        Deletes += other.Deletes; WordDeletes += other.WordDeletes; Pastes += other.Pastes;
        Undo += other.Undo; Redo += other.Redo; Cuts += other.Cuts; Unresolved += other.Unresolved;
        Lost += other.Lost; Commits += other.Commits; Cancels += other.Cancels; ActiveMs += other.ActiveMs;
        Whitespace += other.Whitespace; Injected += other.Injected;
        KeyPresses += other.KeyPresses; KeyActiveMs += other.KeyActiveMs;
    }
    public static Counters Sum(IEnumerable<Counters> rows)
    {
        var result = new Counters(); foreach (var row in rows) result.Add(row); return result;
    }
}

public readonly record struct BucketKey(long UtcMinute, string LocalDate, int Hour, int Offset, string Zone, string App, string Profile);
public sealed record MetricRow(BucketKey Key, Counters Counts);
public sealed record FlushBatch(string Id, IReadOnlyList<MetricRow> Rows, IReadOnlyList<TypingSession> Sessions, IReadOnlyList<KeyMetricRow>? Keys = null, IReadOnlyList<MouseMetricRow>? Mouse = null);
public sealed record TypingSession(string Id, DateTimeOffset Start, DateTimeOffset End, long Gross, long ActiveMs, string Reason, long KeyPresses = 0, long KeyActiveMs = 0);
public readonly record struct InputContext(string App, string Profile, string Field);

/// <summary>Only a bounded final grapheme survives between events; never persists text.</summary>
public sealed class GraphemeStream
{
    private readonly char[] tail = new char[256];
    private int length;
    private char pendingHigh;
    public void Reset() { Array.Clear(tail); length = 0; pendingHigh = '\0'; }
    public (int Delta, int White, bool Overflow) Append(ReadOnlySpan<char> fragment)
    {
        if (fragment.Length > 4096) { Reset(); return (0, 0, true); }
        var extra = pendingHigh == '\0' ? 0 : 1;
        var combined = new char[length + extra + fragment.Length];
        tail.AsSpan(0, length).CopyTo(combined);
        if (extra != 0) combined[length] = pendingHigh;
        fragment.CopyTo(combined.AsSpan(length + extra));
        var finalHigh = combined.Length > 0 && char.IsHighSurrogate(combined[^1]) ? combined[^1] : '\0';
        var usable = combined.Length - (finalHigh == '\0' ? 0 : 1);
        // The transient string is required by StringInfo; it is never logged or queued.
        var text = new string(combined, 0, usable);
        var starts = StringInfo.ParseCombiningCharacters(text);
        var delta = starts.Length - (length > 0 ? 1 : 0);
        var white = 0;
        for (var i = 0; i < starts.Length; i++)
            if ((i != 0 || length == 0) && char.IsWhiteSpace(text[starts[i]])) white++;
        Reset();
        pendingHigh = finalHigh;
        var overflow = false;
        if (starts.Length > 0)
        {
            var last = starts[^1]; var size = text.Length - last;
            if (size <= tail.Length) { text.AsSpan(last).CopyTo(tail); length = size; }
            else overflow = true;
        }
        Array.Clear(combined);
        return (delta, white, overflow);
    }
}

public enum EditAction { Backspace, Delete, WordDelete, Paste, Navigation, Cut, Undo, Redo, UnknownChange }
