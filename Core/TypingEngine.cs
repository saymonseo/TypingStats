namespace TypingStats.Core;

/// <summary>Serialized by its owner. Stores counters and bounded Unicode state, never raw history.</summary>
public sealed class TypingEngine
{
    private readonly Dictionary<BucketKey, Counters> pending = new();
    private readonly List<TypingSession> closedSessions = new();
    private readonly GraphemeStream stream = new();
    private InputContext? context;
    private long remaining;
    private long lastTextMono;
    private DateTimeOffset lastTextUtc;
    private DateTimeOffset? activeCursor;
    private long activeCursorMono;
    private long activeEndMono;
    private InputContext activeContext;
    private string? sessionId;
    private DateTimeOffset sessionStart;
    private DateTimeOffset sessionEnd;
    private long sessionGross, sessionActive;
    public int IdleSeconds { get; set; } = 5;
    public int SessionSeconds { get; set; } = 60;
    public long PendingEstimateBytes => pending.Count * 400L;

    private Counters At(InputContext ctx, DateTimeOffset utc)
    {
        var local = utc.ToLocalTime();
        var key = new BucketKey(utc.ToUnixTimeSeconds() / 60 * 60,
            local.ToString("yyyy-MM-dd"), local.Hour, (int)local.Offset.TotalMinutes,
            TimeZoneInfo.Local.Id, ctx.App, ctx.Profile);
        if (!pending.TryGetValue(key, out var value)) pending[key] = value = new Counters();
        return value;
    }
    public void SetContext(InputContext ctx, DateTimeOffset utc, long mono)
    {
        if (context != ctx)
        {
            Advance(utc, mono); activeEndMono = mono; activeCursor = null;
            BreakSeries(); context = ctx;
        }
    }
    public void Key(InputContext ctx, DateTimeOffset utc, long mono, bool repeat, bool injected = false)
    {
        SetContext(ctx, utc, mono); var c = At(ctx, utc);
        if (injected) c.Injected++; else if (repeat) c.Repeats++; else c.PrimaryKeys++;
    }
    public void Text(InputContext ctx, DateTimeOffset utc, long mono, ReadOnlySpan<char> fragment, bool observed)
    {
        SetContext(ctx, utc, mono); Advance(utc, mono);
        if (lastTextMono != 0 && mono - lastTextMono > SessionSeconds * 1000L)
        { CloseSession("idle"); BreakSeries(); }
        var parsed = stream.Append(fragment); var c = At(ctx, utc);
        if (parsed.Overflow) { c.Unresolved++; BreakSeries(); }
        AddCount(ctx, utc, mono, parsed.Delta, parsed.White, observed);
    }
    public void CommitCount(InputContext ctx, DateTimeOffset utc, long mono, int count, int whitespace)
    {
        SetContext(ctx, utc, mono); Advance(utc, mono);
        var c = At(ctx, utc);
        if (count == 0) c.Cancels++; else c.Commits++;
        stream.Reset(); AddCount(ctx, utc, mono, count, whitespace, true);
    }
    private void AddCount(InputContext ctx, DateTimeOffset utc, long mono, int count, int white, bool observed)
    {
        if (count <= 0) return;
        var c = At(ctx, utc);
        if (observed) c.Observed += count; else c.Estimated += count;
        c.Whitespace += white; remaining += count;
        sessionId ??= Guid.NewGuid().ToString("N");
        if (sessionGross == 0) sessionStart = utc;
        sessionGross += count; sessionEnd = utc;
        lastTextMono = mono; lastTextUtc = utc;
        activeContext = ctx; activeCursor = utc; activeCursorMono = mono;
        activeEndMono = mono + IdleSeconds * 1000L;
    }
    public void Commit(InputContext ctx, DateTimeOffset utc, long mono, ReadOnlySpan<char> result)
    {
        SetContext(ctx, utc, mono); var c = At(ctx, utc);
        if (result.IsEmpty) { c.Cancels++; BreakSeries(); return; }
        c.Commits++; Text(ctx, utc, mono, result, true);
    }
    public void Edit(InputContext ctx, DateTimeOffset utc, long mono, EditAction action)
    {
        SetContext(ctx, utc, mono);
        var c = At(ctx, utc);
        if (mono - lastTextMono > SessionSeconds * 1000L) BreakSeries();
        switch (action)
        {
            case EditAction.Backspace:
                c.Backspaces++;
                if (remaining > 0) { remaining--; c.LinkedBackspaces++; }
                stream.Reset(); // We do not retain deleted text or a whole edit buffer.
                break;
            case EditAction.Delete: c.Deletes++; BreakSeries(); break;
            case EditAction.WordDelete: c.WordDeletes++; BreakSeries(); break;
            case EditAction.Paste: c.Pastes++; BreakSeries(); break;
            case EditAction.Cut: c.Cuts++; BreakSeries(); break;
            case EditAction.Undo: c.Undo++; BreakSeries(); break;
            case EditAction.Redo: c.Redo++; BreakSeries(); break;
            default: BreakSeries(); break;
        }
    }
    public void Unresolved(InputContext ctx, DateTimeOffset utc)
    { At(ctx, utc).Unresolved++; BreakSeries(); }
    public void Lost(InputContext ctx, DateTimeOffset utc, long count)
    { At(ctx, utc).Lost += count; BreakSeries(); }
    public void BreakSeries() { remaining = 0; stream.Reset(); }
    public void Advance(DateTimeOffset utc, long mono)
    {
        if (activeCursor is { } cursor)
        {
            var elapsed = Math.Max(0, Math.Min(mono, activeEndMono) - activeCursorMono);
            // UTC boundaries are apportioned; elapsed itself is measured with monotonic time.
            while (elapsed > 0)
            {
                var nextMinute = DateTimeOffset.FromUnixTimeSeconds(cursor.ToUnixTimeSeconds() / 60 * 60 + 60);
                var portion = Math.Min(elapsed, Math.Max(1, (long)(nextMinute - cursor).TotalMilliseconds));
                At(activeContext, cursor).ActiveMs += portion; sessionActive += portion;
                cursor = cursor.AddMilliseconds(portion); activeCursorMono += portion; elapsed -= portion;
            }
            activeCursor = cursor;
            if (mono >= activeEndMono) activeCursor = null;
        }
        if (sessionId != null && mono - lastTextMono > SessionSeconds * 1000L)
        { CloseSession("idle"); BreakSeries(); }
    }
    public void Stop(DateTimeOffset utc, long mono, string reason)
    {
        Advance(utc, mono); activeCursor = null; CloseSession(reason); BreakSeries(); context = null;
    }
    public void EndContext(DateTimeOffset utc, long mono)
    { Advance(utc, mono); activeCursor = null; BreakSeries(); context = null; }
    private void CloseSession(string reason)
    {
        if (sessionId != null)
            closedSessions.Add(new TypingSession(sessionId, sessionStart, sessionEnd, sessionGross, sessionActive, reason));
        sessionId = null; sessionGross = 0; sessionActive = 0;
    }
    public IReadOnlyList<MetricRow> Peek() => pending.Select(x => new MetricRow(x.Key, x.Value.Copy())).ToArray();
    public FlushBatch Drain()
    {
        var batch = new FlushBatch(Guid.NewGuid().ToString("N"), Peek(), closedSessions.ToArray());
        pending.Clear(); closedSessions.Clear(); return batch;
    }
}
