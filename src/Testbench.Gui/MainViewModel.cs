using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using Testbench.Core.Config;
using Testbench.Core.Deploy;
using Testbench.Core.Diagnostics;
using Testbench.Core.Model;
using Testbench.Core.Report;
using Testbench.Core.Run;
using Testbench.Core.Store;

// System.Windows has its own VisualState (the animation concept). Ours is the
// answer to "has a human looked at this", which is not remotely the same thing.
using VisualState = Testbench.Core.Model.VisualState;

namespace Testbench.Gui;

public enum LogKind { Info, Detail, Good, Warn, Bad }

public sealed record LogEntry(string Text, LogKind Kind);

/// <summary>One selectable game version in the left column.</summary>
public sealed class VersionItem : Notifier
{
    private bool _selected;

    public string Id { get; init; } = "";
    public bool Installed { get; init; }
    public string? Notes { get; init; }

    public bool IsSelected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public string Label => Installed ? Id : $"{Id} (fehlt)";
}

/// <summary>A finished run, as one card on the right.</summary>
public sealed class RunCard
{
    public RunCard(RunRecord run)
    {
        Run = run;
        Title = $"{run.VersionId} / {run.Stage.ToString().ToLowerInvariant()}";
        Status = run.StatusText;
        Ok = run.Status == RunStatus.Ok;

        var a = run.Analysis;
        Detail = run.Status is RunStatus.Missing or RunStatus.SetupError
            ? run.Note ?? ""
            : $"{a.GameVersion}\n" +
              $"Mod {run.ModName} {run.ModVersion}: {(a.ModLoaded ? "geladen" : "NICHT GELADEN")}, " +
              $"Harmony {(a.HarmonyApplied ? "ja" : "nein")}\n" +
              $"ERR {a.Errors}  EXC {a.Exceptions}  XML {a.XmlProblems}  ignoriert {a.Ignored}\n" +
              string.Join("\n", run.Dependencies.Select(d =>
                  $"{d.Folder}: {(d.Problem is null ? "geladen" : d.Problem)}"));
    }

    public RunRecord Run { get; }
    public string Title { get; }
    public string Status { get; }
    public string Detail { get; }
    public bool Ok { get; }
    public string RunId => Run.Id;
    public string LogPath => Run.LogPath;
}

/// <summary>A GUI run whose visual question nobody has answered yet.</summary>
public sealed class PendingItem
{
    public PendingItem(RunRecord run)
    {
        Run = run;
        Header = $"{run.ModDisplayName} {run.ModVersion} - {run.VersionId} / {run.Variant}";
        Question = run.VisualQuestion ?? "Sah/verhielt sich alles wie erwartet?";
        Evidence = run.EvidenceOk switch
        {
            true => $"{run.EvidenceLabel}: ja",
            false => $"{run.EvidenceLabel}: NEIN ({string.Join(", ", run.MissingEvidence)})",
            _ => $"{run.EvidenceLabel ?? "Lognachweis"}: kein Muster konfiguriert",
        };
        EvidenceOk = run.EvidenceOk != false;
    }

    public RunRecord Run { get; }
    public string Header { get; }
    public string Question { get; }
    public string Evidence { get; }
    public bool EvidenceOk { get; }
}

/// <summary>
/// The whole window's state. Deliberately one class: this is a tool window with
/// one job, and splitting it across a framework would add more machinery than it
/// removes.
/// </summary>
public sealed class MainViewModel : Notifier
{
    private readonly string _machinePath;
    private CancellationTokenSource? _cancel;
    private string? _tailPath;
    private long _tailPos;

    public MainViewModel()
    {
        _machinePath = ConfigStore.ResolveMachinePath(null);
        try
        {
            Machine = ConfigStore.LoadMachine(_machinePath);
            ConfigProblem = null;
        }
        catch (ConfigException ex)
        {
            // A window that just closes on a bad config is useless. Show the
            // problem and stay open so doctor and the path are still visible.
            Machine = new MachineConfig();
            ConfigProblem = ex.Message;
        }

        Store = new RunStore(Machine);
        LoadMods();
        RestoreUiState();
        RefreshPending();
    }

    public MachineConfig Machine { get; }
    public RunStore Store { get; }
    public string? ConfigProblem { get; }
    public string MachinePath => _machinePath;

    public ObservableCollection<ModConfig> Mods { get; } = new();
    public ObservableCollection<VersionItem> Versions { get; } = new();
    public ObservableCollection<LogEntry> LogLines { get; } = new();
    public ObservableCollection<RunCard> Results { get; } = new();
    public ObservableCollection<PendingItem> Pending { get; } = new();

    // ---- selection -------------------------------------------------------

    private ModConfig? _selectedMod;
    public ModConfig? SelectedMod
    {
        get => _selectedMod;
        set
        {
            if (!Set(ref _selectedMod, value)) return;
            RefreshVariants();
            RefreshProfiles();
            RefreshReport();
            OnChanged(nameof(CanRun));
        }
    }

    public ObservableCollection<ModVariant> Variants { get; } = new();

    private ModVariant? _selectedVariant;
    public ModVariant? SelectedVariant
    {
        get => _selectedVariant;
        set { if (Set(ref _selectedVariant, value)) RefreshReport(); }
    }

    public ObservableCollection<TestProfile> Profiles { get; } = new();

    private TestProfile? _selectedProfile;
    public TestProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!Set(ref _selectedProfile, value) || value is null) return;
            ApplyProfile(value);
        }
    }

    private bool _stageHeadless = true;
    public bool StageHeadless
    {
        get => _stageHeadless;
        set { if (Set(ref _stageHeadless, value)) OnChanged(nameof(CanRun)); }
    }

    private bool _stageGui;
    public bool StageGui
    {
        get => _stageGui;
        set { if (Set(ref _stageGui, value)) OnChanged(nameof(CanRun)); }
    }

    private bool _skipDeploy;
    public bool SkipDeploy
    {
        get => _skipDeploy;
        set => Set(ref _skipDeploy, value);
    }

    // ---- state -----------------------------------------------------------

    private bool _busy;
    public bool Busy
    {
        get => _busy;
        set
        {
            if (!Set(ref _busy, value)) return;
            OnChanged(nameof(CanRun));
            OnChanged(nameof(NotBusy));
        }
    }

    public bool NotBusy => !Busy;

    public bool CanRun => !Busy && SelectedMod is not null && (StageHeadless || StageGui);

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set => Set(ref _statusText, value);
    }

    private string _testedVersions = "";
    public string TestedVersions
    {
        get => _testedVersions;
        set => Set(ref _testedVersions, value);
    }

    private string _reportSummary = "";
    public string ReportSummary
    {
        get => _reportSummary;
        set => Set(ref _reportSummary, value);
    }

    // ---- actions ---------------------------------------------------------

    public async Task RunAsync()
    {
        if (SelectedMod is null || SelectedVariant is null) return;

        var mod = SelectedMod;
        var variant = SelectedVariant;
        var versions = Versions.Where(v => v.IsSelected).Select(v => v.Id).ToList();
        if (versions.Count == 0)
        {
            Log("Keine Version ausgewaehlt.", LogKind.Warn);
            return;
        }

        var stages = new List<TestStage>();
        if (StageHeadless) stages.Add(TestStage.Headless);
        if (StageGui) stages.Add(TestStage.Gui);

        var running = GameLauncher.RunningInstances();
        if (running.Length > 0)
        {
            Log($"7DaysToDie laeuft schon (PID {running[0].Id}). Erst beenden.", LogKind.Bad);
            foreach (var p in running) p.Dispose();
            return;
        }

        Busy = true;
        Results.Clear();
        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;
        var skipDeploy = SkipDeploy;

        try
        {
            await Task.Run(() =>
            {
                var what = $"{mod.ModId} {string.Join(",", versions)}";
                using var runLock = RunLock.TryAcquire(Machine.StateRoot, "GUI", what, out var holder);
                if (runLock is null)
                {
                    var who = holder is null ? "Ein anderer Lauf" : $"{holder.Owner} (PID {holder.Pid})";
                    Log($"Blockiert: {who} macht gerade '{holder?.What}'.", LogKind.Bad);
                    return;
                }

                var runner = new TestRunner(Machine, Store, m => Log(m, LogKind.Detail));

                foreach (var version in versions)
                {
                    foreach (var stage in stages)
                    {
                        if (token.IsCancellationRequested) return;

                        SetStatus($"{mod.ModId} / {variant.Name} / {version} / {stage.ToString().ToLowerInvariant()}");
                        Log($"=== {version} / {stage.ToString().ToLowerInvariant()} ===", LogKind.Info);
                        if (stage == TestStage.Gui)
                            Log("Das Spiel startet mit Fenster. Der Lauf endet, wenn du es schliesst.", LogKind.Warn);

                        var opts = new RunOptions
                        {
                            SkipDeploy = skipDeploy,
                            // Always deferred: the question belongs in the panel at
                            // the bottom, with the mod's own wording, not in a modal
                            // dialog that pops up while the game is still closing.
                            Visual = VisualMode.Defer,
                            LogPathReady = StartTail,
                        };

                        var run = runner.Run(mod, variant, version, stage, opts, token);
                        StopTail();
                        AddResult(run);

                        Log(run.StatusText, run.Status == RunStatus.Ok ? LogKind.Good : LogKind.Bad);
                    }
                }
            }, token);
        }
        catch (OperationCanceledException)
        {
            Log("Abgebrochen.", LogKind.Warn);
        }
        catch (Exception ex)
        {
            Log($"{ex.GetType().Name}: {ex.Message}", LogKind.Bad);
        }
        finally
        {
            StopTail();
            Busy = false;
            SetStatus("");
            RefreshPending();
            RefreshReport();
        }
    }

    public void Cancel()
    {
        _cancel?.Cancel();
        Log("Abbruch angefordert. Ein laufender Start wird beendet.", LogKind.Warn);
    }

    public void RunDoctor()
    {
        LogLines.Clear();
        Log($"Konfiguration: {_machinePath}", LogKind.Info);
        foreach (var c in Doctor.Run(Machine, _machinePath))
        {
            var kind = c.Level switch
            {
                CheckLevel.Ok => LogKind.Detail,
                CheckLevel.Warn => LogKind.Warn,
                _ => LogKind.Bad,
            };
            Log($"[{c.Area}] {c.Message}", kind);
            if (c.Fix is not null && c.Level != CheckLevel.Ok) Log($"    -> {c.Fix}", LogKind.Info);
        }
    }

    /// <summary>Answers a pending visual check. Only a human can do this.</summary>
    public void Answer(PendingItem item, bool ok, string? note = null)
    {
        item.Run.Visual = ok ? VisualState.Ok : VisualState.Failed;
        item.Run.VisualAt = DateTimeOffset.Now;
        if (!string.IsNullOrWhiteSpace(note)) item.Run.VisualNote = note;
        Store.Save(item.Run);

        Log($"{item.Run.VersionId}: Sichtpruefung {(ok ? "bestanden" : "NICHT bestanden")}",
            ok ? LogKind.Good : LogKind.Bad);

        if (ok && item.Run.EvidenceOk == false)
            Log($"Achtung: {item.Run.EvidenceLabel} fehlt trotzdem. Der Lauf zaehlt nicht als vollstaendig bestanden.",
                LogKind.Warn);

        RefreshPending();
        RefreshReport();
    }

    public string? WriteReport()
    {
        if (SelectedMod is null || SelectedVariant is null) return null;
        var modVersion = ModInfoReader.Read(SelectedMod.VariantSource(SelectedVariant)).Version;
        var versions = Versions.Where(v => v.IsSelected).Select(v => v.Id).ToList();
        if (versions.Count == 0) versions = Machine.Versions.Select(v => v.Id).ToList();

        var report = ReportBuilder.Build(SelectedMod, SelectedVariant.Name, modVersion, Store, versions);
        var path = ReportBuilder.Write(report, Machine);
        Log($"Report: {path}", LogKind.Good);
        return path;
    }

    public void RefreshPending()
    {
        Dispatch(() =>
        {
            Pending.Clear();
            foreach (var r in Store.PendingVisual()) Pending.Add(new PendingItem(r));
        });
    }

    public void RefreshReport()
    {
        if (SelectedMod is null || SelectedVariant is null)
        {
            TestedVersions = "";
            ReportSummary = "";
            return;
        }

        var source = SelectedMod.VariantSource(SelectedVariant);
        var modVersion = Directory.Exists(source) ? ModInfoReader.Read(source).Version : "";
        var versions = Versions.Where(v => v.IsSelected).Select(v => v.Id).ToList();
        if (versions.Count == 0) versions = Machine.Versions.Select(v => v.Id).ToList();

        var report = ReportBuilder.Build(SelectedMod, SelectedVariant.Name, modVersion, Store, versions);
        TestedVersions = report.TestedVersions;

        var lines = report.Rows.Select(r =>
            $"{r.VersionId}: Stufe 1 {(r.Headless?.StatusText ?? "UNGETESTET")}, " +
            $"Stufe 2 {(r.GuiOk ? "OK" : r.GuiNote)}");
        ReportSummary = $"Mod-Version {(modVersion.Length > 0 ? modVersion : "unbekannt")}\n" +
                        string.Join("\n", lines);
    }

    // ---- log tailing -----------------------------------------------------

    /// <summary>
    /// Follows the game's log file while it runs. Without this a 35 second headless
    /// start looks like the tool is doing nothing.
    /// </summary>
    private void StartTail(string path)
    {
        _tailPath = path;
        _tailPos = 0;
        _ = Task.Run(async () =>
        {
            while (_tailPath == path)
            {
                await Task.Delay(600);
                if (!File.Exists(path)) continue;
                try
                {
                    // The game holds the file open, so sharing has to be allowed.
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    if (fs.Length <= _tailPos) continue;
                    fs.Seek(_tailPos, SeekOrigin.Begin);
                    using var sr = new StreamReader(fs);
                    var text = await sr.ReadToEndAsync();
                    _tailPos = fs.Position;

                    foreach (var line in text.Split('\n'))
                    {
                        var l = line.TrimEnd('\r');
                        if (l.Length == 0) continue;
                        Log(l, ClassifyGameLine(l));
                    }
                }
                catch (IOException) { }
            }
        });
    }

    private void StopTail() => _tailPath = null;

    private static LogKind ClassifyGameLine(string line)
    {
        if (line.Contains(" EXC ") || line.Contains("Exception:")) return LogKind.Bad;
        if (line.Contains(" ERR ")) return LogKind.Bad;
        if (line.Contains(" WRN ")) return LogKind.Warn;
        if (line.Contains("[MODS]") || line.Contains("Harmony patches applied")) return LogKind.Good;
        return LogKind.Detail;
    }

    // ---- plumbing --------------------------------------------------------

    private void LoadMods()
    {
        Mods.Clear();
        var registered = ConfigStore.LoadRegisteredMods(Machine, out var missing);
        foreach (var (mod, _) in registered) Mods.Add(mod);
        foreach (var m in missing) Log($"Registrierte Mod-Konfiguration fehlt: {m}", LogKind.Warn);

        Versions.Clear();
        foreach (var v in Machine.Versions)
        {
            Versions.Add(new VersionItem
            {
                Id = v.Id,
                Installed = File.Exists(Path.Combine(Machine.GameDir(v.Id), "7DaysToDie.exe")),
                Notes = v.Notes,
            });
        }

        SelectedMod ??= Mods.FirstOrDefault();
    }

    private void RefreshVariants()
    {
        Variants.Clear();
        foreach (var v in SelectedMod?.Variants ?? new List<ModVariant>()) Variants.Add(v);
        SelectedVariant = Variants.FirstOrDefault();
    }

    private void RefreshProfiles()
    {
        Profiles.Clear();
        foreach (var p in SelectedMod?.Profiles ?? new List<TestProfile>()) Profiles.Add(p);
        _selectedProfile = null;
        OnChanged(nameof(SelectedProfile));
    }

    private void ApplyProfile(TestProfile profile)
    {
        if (profile.Variant is not null)
        {
            var v = Variants.FirstOrDefault(x => string.Equals(x.Name, profile.Variant, StringComparison.OrdinalIgnoreCase));
            if (v is not null) SelectedVariant = v;
        }

        var wanted = profile.Versions.Count == 0
            ? Versions.Select(v => v.Id).ToList()
            : profile.Versions;
        foreach (var v in Versions) v.IsSelected = wanted.Contains(v.Id, StringComparer.OrdinalIgnoreCase);

        StageHeadless = profile.Stages.Contains(TestStage.Headless);
        StageGui = profile.Stages.Contains(TestStage.Gui);
        RefreshReport();
    }

    private void AddResult(RunRecord run) => Dispatch(() => Results.Add(new RunCard(run)));

    private void Log(string text, LogKind kind = LogKind.Info) => Dispatch(() =>
    {
        LogLines.Add(new LogEntry(text, kind));
        // A whole headless startup is over 1300 lines; three runs would make the
        // list unusable to scroll.
        while (LogLines.Count > 4000) LogLines.RemoveAt(0);
    });

    private void SetStatus(string text) => Dispatch(() => StatusText = text);

    private static void Dispatch(Action action)
    {
        var app = Application.Current;
        if (app is null) { action(); return; }
        if (app.Dispatcher.CheckAccess()) action();
        else app.Dispatcher.BeginInvoke(action);
    }

    // ---- remembering the last selection ----------------------------------

    private sealed record UiState(string? ModId, string? Variant, List<string> Versions, bool Headless, bool Gui);

    private string UiStatePath => Path.Combine(Machine.StateRoot, "gui-state.json");

    private void RestoreUiState()
    {
        try
        {
            if (!File.Exists(UiStatePath)) { SelectDefaults(); return; }
            var state = JsonSerializer.Deserialize<UiState>(File.ReadAllText(UiStatePath), ConfigStore.Json);
            if (state is null) { SelectDefaults(); return; }

            var mod = Mods.FirstOrDefault(m => string.Equals(m.ModId, state.ModId, StringComparison.OrdinalIgnoreCase));
            if (mod is not null) SelectedMod = mod;

            var variant = Variants.FirstOrDefault(v => string.Equals(v.Name, state.Variant, StringComparison.OrdinalIgnoreCase));
            if (variant is not null) SelectedVariant = variant;

            foreach (var v in Versions) v.IsSelected = state.Versions.Contains(v.Id, StringComparer.OrdinalIgnoreCase);
            StageHeadless = state.Headless;
            StageGui = state.Gui;

            if (Versions.All(v => !v.IsSelected)) SelectDefaults();
        }
        catch (Exception)
        {
            SelectDefaults();
        }
        RefreshReport();
    }

    private void SelectDefaults()
    {
        foreach (var v in Versions) v.IsSelected = v.Installed;
        StageHeadless = true;
        StageGui = false;
    }

    public void SaveUiState()
    {
        try
        {
            var state = new UiState(
                SelectedMod?.ModId,
                SelectedVariant?.Name,
                Versions.Where(v => v.IsSelected).Select(v => v.Id).ToList(),
                StageHeadless,
                StageGui);
            ConfigStore.WriteAtomic(UiStatePath, JsonSerializer.Serialize(state, ConfigStore.Json));
        }
        catch (Exception)
        {
            // Losing the last selection is a nuisance, not a failure worth a dialog.
        }
    }
}

/// <summary>Minimal INotifyPropertyChanged so the GUI needs no MVVM package.</summary>
public abstract class Notifier : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnChanged(name);
        return true;
    }

    protected void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
