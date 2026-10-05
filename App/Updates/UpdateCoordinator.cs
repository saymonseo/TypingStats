using TypingStats.Core;

namespace TypingStats.App.Updates;

internal sealed class UpdateCoordinator : IDisposable
{
    private readonly Collector collector;
    private readonly Settings settings;
    private readonly IUpdateTransport transport;
    private readonly Func<Collector, string, Task> launch;
    private readonly Func<DateTimeOffset> clock;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly DateTimeOffset startup;
    private DateTimeOffset retryAfter;
    private bool retryPending;
    private ReleaseUpdate? prepared;
    private string? staged;
    private bool disposed;
    public string Status { get; private set; } = "Автопроверка после запуска и раз в 6 часов";
    public ReleaseUpdate? Available { get; private set; }
    public bool Busy => gate.CurrentCount == 0;
    public bool Installing { get; private set; }
    public event Action? RestartRequested;
    public event Action<string>? Notification;
    public UpdateCoordinator(Collector collector, IUpdateTransport? transport = null, Func<Collector, string, Task>? launch = null, Func<DateTimeOffset>? clock = null)
    {
        this.collector = collector; settings = collector.Settings;
        this.transport = transport ?? new GitHubUpdates(); this.launch = launch ?? GitHubUpdates.Launch; this.clock = clock ?? (() => DateTimeOffset.UtcNow); startup = this.clock();
        if (settings.AutoUpdateAttemptVersion != null)
        {
            if (AutomaticUpdatePolicy.ShouldSuppress(settings.AutoUpdateAttemptVersion, GitHubUpdates.Current))
            { settings.FailedAutoUpdateVersion = settings.AutoUpdateAttemptVersion; Status = "Прежняя версия восстановлена. Автоустановка проблемного релиза отключена."; }
            settings.AutoUpdateAttemptVersion = null; TrySave();
        }
    }
    private void TrySave() { try { settings.Save(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Status = "Не удалось сохранить настройки обновления: " + e.GetType().Name; } }
    public async Task<ReleaseUpdate?> Check(CancellationToken cancellation)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
        await gate.WaitAsync(linked.Token);
        try { return await CheckCore(linked.Token); }
        finally { gate.Release(); }
    }
    private async Task<ReleaseUpdate?> CheckCore(CancellationToken token)
    {
        Status = "Проверяем GitHub Releases…";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var update = await transport.Check(settings.IncludePreviewUpdates, timeout.Token);
        retryPending = false;
        settings.LastUpdateCheckUtc = clock(); TrySave(); Available = update;
        Status = update == null ? "Установлена последняя доступная версия" : "Доступна версия " + update.Version.ToString(3);
        return update;
    }
    public async Task Install(ReleaseUpdate update, IProgress<int> progress, CancellationToken cancellation)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
        await gate.WaitAsync(linked.Token);
        try
        {
            await Prepare(update, progress, linked.Token); linked.Token.ThrowIfCancellationRequested();
            await Commit(update);
        }
        catch
        {
            settings.FailedAutoUpdateVersion = update.Version.ToString(3); prepared = null; staged = null; TrySave(); throw;
        }
        finally { gate.Release(); }
    }
    private async Task Prepare(ReleaseUpdate update, IProgress<int> progress, CancellationToken token)
    {
        if (prepared?.Sha256 == update.Sha256 && staged != null) return;
        Status = "Скачиваем и проверяем обновление…";
        var path = await transport.Download(update, progress, token, settings.DataDirectory);
        prepared = update; staged = path;
        Status = "Обновление готово. Установка после закрытия окна и минуты без ввода";
    }
    private async Task Commit(ReleaseUpdate update)
    {
        Installing = true;
        try
        {
            // Persist before launch: a rollback to this version will suppress the faulty release.
            settings.AutoUpdateAttemptVersion = update.Version.ToString(3); settings.Save();
            Status = "Сохраняем историю и устанавливаем обновление…";
            await launch(collector, staged!); prepared = null; staged = null;
        }
        catch { settings.AutoUpdateAttemptVersion = null; TrySave(); throw; }
        finally { Installing = false; }
    }
    public async Task Tick(Func<bool> safeToInstall)
    {
        if (disposed || !settings.AutomaticUpdates || clock() < retryAfter || !gate.Wait(0)) return;
        var restart = false;
        try
        {
            if (prepared != null && prepared.Preview && !settings.IncludePreviewUpdates) { prepared = null; staged = null; }
            if (prepared == null)
            {
                if (!retryPending && !AutomaticUpdatePolicy.CheckDue(true, clock(), startup, settings.LastUpdateCheckUtc)) return;
                var update = await CheckCore(lifetime.Token);
                if (update == null || update.Version.ToString(3) == settings.FailedAutoUpdateVersion) return;
                await Prepare(update, new Progress<int>(), lifetime.Token);
                Notification?.Invoke("Скачана версия " + update.Version.ToString(3) + ". Она установится в трее после минуты без ввода.");
            }
            // Re-evaluate after the download: the user may have opened a dialog or resumed input.
            if (prepared != null && settings.AutomaticUpdates && safeToInstall())
            { await Commit(prepared); restart = true; }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception e)
        {
            if (Installing || prepared != null)
            {
                if (prepared != null) settings.FailedAutoUpdateVersion = prepared.Version.ToString(3);
                prepared = null; staged = null; TrySave();
                Notification?.Invoke("Автообновление не установлено. Текущая версия продолжает работать; подробности в «Обновления». ");
            }
            retryAfter = clock().Add(AutomaticUpdatePolicy.RetryInterval);
            retryPending = true;
            Status = "Не удалось обновиться: " + e.Message;
        }
        finally { gate.Release(); }
        if (restart && !disposed) RestartRequested?.Invoke();
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; lifetime.Cancel(); transport.Dispose();
        // An in-flight asynchronous operation still needs the semaphore/token until it unwinds.
    }
}
