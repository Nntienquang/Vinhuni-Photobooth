using Booth.Camera;
using Microsoft.AspNetCore.SignalR;
namespace Booth;
public sealed class BoothEngine(Storage storage, Templates templates, PhotoComposer composer, ICameraProvider camera, IPrintService printer, CloudQueue cloud, IHubContext<BoothHub> hub, ILogger<BoothEngine> logger) : BackgroundService
{
    readonly SemaphoreSlim gate = new(1, 1); readonly SemaphoreSlim uploads = new(1, 1); readonly List<Activity> activity = []; CancellationTokenSource? captureCancel; DateTimeOffset changed = DateTimeOffset.Now; long lastFrame;
    int frameSending;
    Task captureTask = Task.CompletedTask;
    public Settings Settings { get; private set; } = storage.LoadSettings();
    public Session? Current
    {
        get; private set;
    }
    public BoothState State { get; private set; } = BoothState.IDLE;
    public int Countdown
    {
        get; private set;
    }
    public void Log(string message)
    {
        lock (activity)
        {
            activity.Insert(0, new(DateTimeOffset.Now, message));
            if (activity.Count > 80)
                activity.RemoveAt(activity.Count - 1);
        }
        logger.LogInformation("{Activity}", message);
    }
    public async Task<object> Status()
    {
        var sessions = storage.Sessions();
        var today = sessions.Where(s => s.CreatedAt.LocalDateTime.Date == DateTime.Today).ToList();
        Activity[] logs;
        lock (activity)
            logs = activity.ToArray();
        return new
        {
            state = State,
            session = Current,
            camera = await camera.GetStatusAsync(),
            cameraPreferences = (camera as RuntimeCameraProvider)?.Preferences ?? new CameraPreferences("mock"),
            settings = Settings,
            countdown = Countdown,
            customerDisplays = BoothHub.Displays.Count,
            cloudConfigured = cloud.Configured,
            printProvider = printer.Provider,
            printerReady = printer.Ready(Settings.Printer),
            pendingUploads = sessions.Count(s => s.UploadStatus == "pending"),
            sessionsToday = today.Count,
            photosToday = today.Sum(s => s.Shots.Count),
            finalsToday = today.Count(s => s.FinalPath != null),
            printsToday = today.Count(s => s.PrintStatus == "sent"),
            failedPrintsToday = today.Count(s => s.PrintStatus is "failed" or "uncertain"),
            uploadsToday = today.Count(s => s.UploadStatus == "uploaded"),
            activity = logs
        };
    }
    async Task Publish(string name = "systemStatusChanged")
    {
        await hub.Clients.All.SendAsync(name, await Status());
    }
    async Task Set(BoothState state)
    {
        State = state;
        changed = DateTimeOffset.Now;
        if (Current is not null)
        {
            Current.State = state;
            storage.Save(Current);
        }
        await Publish("sessionChanged");
    }
    public async Task Exclusive(Func<Task> action)
    {
        if (!await gate.WaitAsync(0))
            throw new InvalidOperationException("Booth is busy");
        try
        {
            await action();
        }
        finally { gate.Release(); }
    }
    public static void RequireCameraIdle(BoothState state, Session? session)
    {
        if (session is not null || state is not (BoothState.IDLE or BoothState.MAINTENANCE))
            throw new InvalidOperationException("Cannot change camera while a photo session is active.");
    }
    public async Task CameraAction(Func<Task> action)
    {
        RequireCameraIdle(State, Current);
        if (!await gate.WaitAsync(0)) throw new InvalidOperationException("Cannot change camera while a photo session is active.");
        try
        {
            RequireCameraIdle(State, Current);
            try { await action(); }
            finally
            {
                if (!(await camera.GetStatusAsync()).Connected) await Set(BoothState.MAINTENANCE);
                await Publish();
            }
        }
        finally { gate.Release(); }
    }
    public async Task DisconnectCamera()
    {
        // Emergency disconnect retains the existing route behavior: cancel the capture,
        // wait for its JPEG transfer to finish/cancel, then release the device.
        captureCancel?.Cancel();
        await captureTask;
        await Exclusive(async () => { await camera.DisconnectAsync(); await Set(BoothState.MAINTENANCE); });
    }
    public async Task RecoverCamera(Func<Task> action)
    {
        await Exclusive(async () =>
        {
            if (State is not (BoothState.IDLE or BoothState.MAINTENANCE))
                throw new InvalidOperationException("Cannot change camera while a photo session is active.");
            try { await action(); }
            finally
            {
                if (!(await camera.GetStatusAsync()).Connected) await Set(BoothState.MAINTENANCE);
                await Publish();
            }
        });
    }
    public async Task Configure(Settings settings) => await Exclusive(async () => { if (State is not (BoothState.IDLE or BoothState.MAINTENANCE)) throw new InvalidOperationException("Finish the session before changing settings"); settings.Validate(); storage.SaveSettings(settings); Settings = settings; await Publish(); });
    public async Task Select() => await Exclusive(async () => { if (State != BoothState.IDLE) throw new InvalidOperationException("Booth unavailable"); if (!(await camera.GetStatusAsync()).Connected) throw new InvalidOperationException("Camera disconnected"); await Set(BoothState.FRAME_SELECTION); });
    public async Task Start(string templateId) => await Exclusive(async () => { if (State != BoothState.FRAME_SELECTION) throw new InvalidOperationException("Select a frame first"); var t = templates.Get(templateId); if (!t.Enabled) throw new InvalidOperationException("Template disabled"); Current = new Session { Template = t }; var dir = storage.SessionDir(Current); Directory.CreateDirectory(dir); File.Copy(Path.Combine(templates.Dir(t.Id), "frame.png"), Path.Combine(dir, "frame.png")); Storage.WriteJson(Path.Combine(dir, "template.json"), t); await camera.StartLiveViewAsync(); Log("Session started " + Current.Id); await Set(BoothState.LIVE_VIEW); });
    public Session Require(string id)
    {
        if (Current is null || Current.Id != id)
            throw new InvalidOperationException("Session is no longer active");
        return Current;
    }
    public async Task Capture(string id)
    {
        if (!await gate.WaitAsync(0))
            throw new InvalidOperationException("Capture already running");
        try
        {
            var s = Require(id);
            SessionRules.Require(s, BoothState.LIVE_VIEW, BoothState.READY);
            captureCancel = new CancellationTokenSource();
            await Set(BoothState.READY);
            captureTask = CaptureLoop(s, captureCancel.Token);
        }
        catch { gate.Release(); throw; }
    }
    async Task CaptureLoop(Session s, CancellationToken token)
    {
        try
        {
            while (s.Shots.Count < s.Template.EffectiveCaptureCount)
            {
                Countdown = Settings.Countdown;
                await Set(BoothState.COUNTDOWN);
                await hub.Clients.All.SendAsync("countdownStarted", Settings.Countdown, token);
                for (var i = Settings.Countdown; i > 0; i--)
                {
                    Countdown = i;
                    await hub.Clients.All.SendAsync("countdownTick", i, token);
                    await Task.Delay(1000, token);
                }
                Countdown = 0;
                await Set(BoothState.CAPTURING);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(25));
                var file = Path.Combine(storage.SessionDir(s), "originals", $"take_{s.Retakes}", $"shot_{s.Shots.Count + 1:00}.jpg");
                var result = await camera.CaptureAsync(file, timeout.Token);
                // Replace the list so realtime serializers never enumerate a list being mutated.
                s.Shots = [.. s.Shots, result];
                storage.Save(s);
                Log($"Shot {s.Shots.Count}/{s.Template.EffectiveCaptureCount} captured");
                await hub.Clients.All.SendAsync("photoCaptured", s.Shots.Count, token);
                if (s.Shots.Count < s.Template.EffectiveCaptureCount)
                {
                    await Set(BoothState.BETWEEN_SHOTS);
                    await Task.Delay(TimeSpan.FromSeconds(Settings.BetweenShots), token);
                }
            }
            await Set(BoothState.ASSIGN);
        }
        catch (Exception e) { Log("Capture failed: " + e.Message); s.Error = e.Message; await Set((await camera.GetStatusAsync()).Connected ? BoothState.ERROR : BoothState.MAINTENANCE); }
        finally { captureCancel?.Dispose(); captureCancel = null; gate.Release(); }
    }
    async Task ComposeCore(Session s)
    {
        var selected = s.SelectedShots;
        if (selected.Count != s.Template.EffectiveSelectionCount || selected.Any(i => i < 0 || i >= s.Shots.Count) || selected.Distinct().Count() != selected.Count)
            throw new InvalidOperationException($"Select exactly {s.Template.EffectiveSelectionCount} photos");
        s.SelectedShots = [.. selected];
        await Set(BoothState.COMPOSING);
        await hub.Clients.All.SendAsync("compositionStarted");
        var originals = selected.Select(index => s.Shots[index]).ToList();
        try
        {
            s.FinalPath = await Task.Run(() => composer.Compose(s.Template, originals, Path.Combine(storage.SessionDir(s), "frame.png"), Path.Combine(storage.SessionDir(s), "final", $"take_{s.Retakes}"), Settings));
            s.Error = null;
            Log("Composition complete");
            await Set(BoothState.FINAL_PREVIEW);
            await hub.Clients.All.SendAsync("compositionFinished");
        }
        catch (Exception e)
        {
            s.Error = e.Message;
            await Set(BoothState.PHOTO_SELECTION); // Keep assignments for retry.
            throw;
        }
    }
    public async Task AssignPhotos(string id, IReadOnlyList<int>? indices) => await Exclusive(async () =>
    {
        var s = Require(id);
        SessionRules.Require(s, BoothState.ASSIGN, BoothState.PHOTO_SELECTION, BoothState.FILTER);
        if (s.Confirmed || s.Shots.Count != s.Template.EffectiveCaptureCount)
            throw new InvalidOperationException("Complete capture before assigning photos");
        if (indices is null || indices.Count != s.Template.EffectiveSelectionCount ||
            indices.Distinct().Count() != indices.Count || indices.Any(i => i < 0 || i >= s.Shots.Count))
            throw new ArgumentException($"Assign exactly {s.Template.EffectiveSelectionCount} distinct captured photos");
        s.SelectedShots = [.. indices]; // List position is the logical slot; photo indices are zero-based.
        await Set(BoothState.FILTER); // Existing Set persists session JSON and publishes the transition.
    });
    public async Task SelectPhotos(string id, IReadOnlyList<int> indices) => await Exclusive(async () =>
    {
        var s = Require(id);
        SessionRules.Require(s, BoothState.PHOTO_SELECTION, BoothState.ASSIGN, BoothState.FILTER);
        if (indices.Count != s.Template.EffectiveSelectionCount || indices.Distinct().Count() != indices.Count || indices.Any(i => i < 0 || i >= s.Shots.Count))
            throw new InvalidOperationException($"Select exactly {s.Template.EffectiveSelectionCount} photos");

        s.SelectedShots = [.. indices];
        storage.Save(s);
        await Publish("sessionChanged");
    });
    public async Task ReorderPhotos(string id, IReadOnlyList<int> indices) => await SelectPhotos(id, indices);
    public async Task Compose(string id) => await Exclusive(async () => { var s = Require(id); SessionRules.Require(s, BoothState.PHOTO_SELECTION, BoothState.ASSIGN, BoothState.FILTER, BoothState.ERROR); if (s.Confirmed) throw new InvalidOperationException("Final locked"); await ComposeCore(s); });
    public async Task Retake(string id) => await Exclusive(async () => { var s = Require(id); SessionRules.Retake(s, Settings); s.Retakes++; s.Shots = []; s.SelectedShots = []; s.FinalPath = null; s.Error = null; await camera.StartLiveViewAsync(); await Set(BoothState.LIVE_VIEW); });
    public async Task Confirm(string id) => await Exclusive(async () => { var s = Require(id); SessionRules.Require(s, BoothState.FINAL_PREVIEW); s.Confirmed = true; s.UploadStatus = Settings.CloudEnabled ? "pending" : "disabled"; storage.Save(s); if (Settings.AutoPrint) await PrintCore(s); else await Set(BoothState.QR_READY); if (Settings.CloudEnabled && cloud.Configured) _ = RetryUploadsSafe(); });
    public async Task ResolvePrint(string id, bool alreadyPrinted) => await Exclusive(async () =>
    {
        var session = Current?.Id == id ? Current : storage.Find(id);
        if (session.PrintStatus != "uncertain") throw new InvalidOperationException("Only uncertain jobs can be resolved");
        session.PrintStatus = alreadyPrinted ? "sent" : "failed";
        session.Error = alreadyPrinted ? null : "Operator checked spooler and marked the job not printed";
        storage.Save(session);
        storage.ResolvePrintJobs(id, session.PrintStatus);
        Log($"Operator resolved print {id}: {session.PrintStatus}");
        await Publish();
    });
    async Task PrintCore(Session s)
    {
        SessionRules.Print(s);
        s.PrintStatus = "sending";
        storage.Save(s);
        var job = storage.BeginPrint(s, Settings);
        await Set(BoothState.PRINTING);
        await hub.Clients.All.SendAsync("printingStarted");
        try
        {
            await printer.PrintAsync(s.FinalPath!, Settings);
            s.PrintStatus = "sent";
            s.Error = null;
            storage.EndPrint(job, "sent", null);
            Log("Print submitted to " + printer.Provider);
            await Set(BoothState.QR_READY);
        }
        catch (Exception e) { s.PrintStatus = "failed"; s.Error = e.Message; storage.EndPrint(job, "failed", e.Message); Log("Print failed: " + e.Message); await Set(BoothState.ERROR); }
        await hub.Clients.All.SendAsync("printingFinished");
    }
    public async Task Print(string id) => await Exclusive(async () => { var s = Require(id); SessionRules.Require(s, BoothState.QR_READY, BoothState.ERROR); await PrintCore(s); });
    public async Task Reprint(string id) => await Exclusive(async () => { var s = storage.Find(id); if (s.PrintStatus is "sending" or "uncertain") throw new InvalidOperationException("Check Windows spooler before acknowledging an uncertain job"); SessionRules.Print(s); var job = storage.BeginPrint(s, Settings); s.PrintStatus = "sending"; storage.Save(s); try { await printer.PrintAsync(s.FinalPath!, Settings); s.PrintStatus = "sent"; storage.EndPrint(job, "sent", null); } catch (Exception e) { s.PrintStatus = "failed"; storage.EndPrint(job, "failed", e.Message); throw; } finally { storage.Save(s); } await Publish(); });
    public async Task Finish() => await Exclusive(async () => { if (Current is null || !Current.Confirmed || State is not (BoothState.QR_READY or BoothState.ERROR)) throw new InvalidOperationException("No confirmed session"); await Set(BoothState.THANK_YOU); });
    public async Task Reset() => await Exclusive(async () => { Current = null; Countdown = 0; await Set((await camera.GetStatusAsync()).Connected ? BoothState.IDLE : BoothState.MAINTENANCE); await hub.Clients.All.SendAsync("boothReset"); });
    public async Task Pause()
    {
        captureCancel?.Cancel();
        if (!await gate.WaitAsync(0))
            return;
        try
        {
            await Set(BoothState.MAINTENANCE);
        }
        finally { gate.Release(); }
    }
    public async Task RetryUploads()
    {
        if (!await uploads.WaitAsync(0))
            return;
        try
        {
            foreach (var stored in storage.Sessions().Where(s => s.UploadStatus == "pending"))
            {
                await cloud.Upload(stored);
                if (Current?.Id == stored.Id)
                {
                    Current.UploadStatus = stored.UploadStatus;
                    Current.ShareUrl = stored.ShareUrl;
                }
                await Publish("cloudUploadFinished");
            }
        }
        finally { uploads.Release(); }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        storage.RecoverPrintJobs();
        foreach (var s in storage.Sessions())
        {
            if (s.PrintStatus == "sending")
            {
                s.PrintStatus = "uncertain";
                storage.Save(s);
            }
            if (!s.Confirmed && s.State is not (BoothState.THANK_YOU or BoothState.IDLE))
            {
                s.State = BoothState.ERROR;
                s.Error = "Bridge restarted; originals preserved";
                storage.Save(s);
            }
        }
        camera.CameraConnected += () => Log("Camera connected");
        camera.CameraDisconnected += () => { captureCancel?.Cancel(); _ = Disconnected(); };
        camera.CameraError += message => { Log("Camera error: " + message); _ = Pause(); };
        camera.LiveViewFrameReceived += bytes => { var now = Environment.TickCount64; if (now - Interlocked.Read(ref lastFrame) < 1000 / Settings.LiveViewFps || Interlocked.CompareExchange(ref frameSending, 1, 0) != 0) return; Interlocked.Exchange(ref lastFrame, now); _ = SendFrame(bytes, stoppingToken); };
        await gate.WaitAsync(stoppingToken);
        try
        {
            await camera.InitializeAsync();
            await camera.ScanAsync();
            if (camera is not RuntimeCameraProvider runtime || runtime.Preferences.AutoConnect)
            {
                await camera.ConnectAsync();
                if (camera is not RuntimeCameraProvider) await camera.StartLiveViewAsync();
            }
            if (!(await camera.GetStatusAsync()).Connected) await Set(BoothState.MAINTENANCE);
        }
        catch (Exception e) { Log("Camera setup: " + e.Message); await Set(BoothState.MAINTENANCE); }
        finally { gate.Release(); }
        var nextUpload = DateTimeOffset.Now;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, stoppingToken);
                var age = (DateTimeOffset.Now - changed).TotalSeconds;
                if ((State == BoothState.THANK_YOU && age > Settings.ThankYouTimeout) || ((State is BoothState.FRAME_SELECTION or BoothState.LIVE_VIEW) && age > Settings.IdleTimeout) || (State == BoothState.FINAL_PREVIEW && age > Settings.PreviewTimeout))
                    await Reset();
                if (State == BoothState.QR_READY && age > Settings.IdleTimeout)
                    await Finish();
                if (cloud.Configured && DateTimeOffset.Now > nextUpload)
                {
                    nextUpload = DateTimeOffset.Now.AddSeconds(30);
                    _ = RetryUploadsSafe();
                }
                await Publish();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogError(e, "Background recovery"); }
        }
    }
    async Task RetryUploadsSafe()
    {
        try
        {
            await RetryUploads();
        }
        catch (Exception e) { logger.LogError(e, "Upload retry failed"); }
    }
    async Task SendFrame(byte[] bytes, CancellationToken token)
    {
        try { await hub.Clients.All.SendAsync("liveViewFrame", Convert.ToBase64String(bytes), token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e) { logger.LogDebug(e, "Live view frame dropped"); }
        finally { Volatile.Write(ref frameSending, 0); }
    }
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        captureCancel?.Cancel();
        try { await captureTask.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) { }
        await camera.StopLiveViewAsync();
        await base.StopAsync(cancellationToken);
    }
    async Task Disconnected()
    {
        Log("Camera disconnected");
        if (await gate.WaitAsync(0))
        {
            try
            {
                await Set(BoothState.MAINTENANCE);
            }
            finally { gate.Release(); }
        }
    }
}
