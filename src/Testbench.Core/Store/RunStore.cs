using System.Text.Json;
using Testbench.Core.Config;
using Testbench.Core.Model;
using Testbench.Core.Run;

namespace Testbench.Core.Store;

/// <summary>
/// Every run as one JSON file under &lt;StateRoot&gt;\runs.
///
/// Replaces gui-verified.json, which kept exactly one entry per
/// mod+version+edition and therefore had no history at all. It also had a
/// mismatch that made the whole thing useless: Start-Gui.ps1 wrote EvidenceOk
/// while Invoke-TestMatrix.ps1 read AtlasOk, so GuiOk was always false and no
/// TESTED_VERSIONS line was ever proposed. One store written and read by one
/// typed model is the fix.
/// </summary>
public sealed class RunStore
{
    private readonly string _dir;

    public RunStore(MachineConfig machine) : this(Path.Combine(machine.StateRoot, "runs")) { }

    public RunStore(string dir)
    {
        _dir = dir;
        Directory.CreateDirectory(_dir);
    }

    public string PathOf(string runId) => Path.Combine(_dir, runId + ".json");

    public static string NewId(string modId, string versionId, TestStage stage) =>
        $"{DateTime.Now:yyyyMMdd-HHmmss}_{Sanitize(modId)}_{Sanitize(versionId)}_{stage.ToString().ToLowerInvariant()}";

    public void Save(RunRecord run)
    {
        if (string.IsNullOrWhiteSpace(run.Id)) throw new InvalidOperationException("RunRecord ohne Id.");
        ConfigStore.WriteAtomic(PathOf(run.Id), JsonSerializer.Serialize(run, ConfigStore.Json));
    }

    public RunRecord? Load(string runId)
    {
        var path = PathOf(runId);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<RunRecord>(File.ReadAllText(path), ConfigStore.Json); }
        catch (JsonException) { return null; }
    }

    /// <summary>Newest first. A corrupt file is skipped, not fatal.</summary>
    public List<RunRecord> All()
    {
        var runs = new List<RunRecord>();
        foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
        {
            try
            {
                var r = JsonSerializer.Deserialize<RunRecord>(File.ReadAllText(file), ConfigStore.Json);
                if (r is not null) runs.Add(r);
            }
            catch (Exception) { }
        }
        return runs.OrderByDescending(r => r.Started).ToList();
    }

    public List<RunRecord> ForMod(string modId) =>
        All().Where(r => string.Equals(r.ModId, modId, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>GUI runs that finished but nobody has answered the visual question for.</summary>
    public List<RunRecord> PendingVisual() =>
        All().Where(r => r.Visual == VisualState.Pending).ToList();

    /// <summary>
    /// Newest run per version for one mod version, matched on mod AND variant AND
    /// mod version. The PowerShell matrix matched on the game version alone, so an
    /// AdamantBlock confirmation would have satisfied a 7 Dashes matrix run.
    /// </summary>
    public RunRecord? LatestFor(string modId, string variant, string versionId, TestStage stage, string? modVersion = null) =>
        All().FirstOrDefault(r =>
            string.Equals(r.ModId, modId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.Variant, variant, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.VersionId, versionId, StringComparison.OrdinalIgnoreCase) &&
            r.Stage == stage &&
            (modVersion is null || string.Equals(r.ModVersion, modVersion, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Takes the old gui-verified.json over. Its entries only ever held the fields
    /// a stage 2 confirmation needs, so the imported records carry no counters and
    /// are marked as imported in the note.
    /// </summary>
    public List<RunRecord> ImportGuiVerified(string jsonPath, string modIdFor)
    {
        var imported = new List<RunRecord>();
        if (!File.Exists(jsonPath)) return imported;

        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return imported;

        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var version = Str(e, "Version");
            var modFolder = Str(e, "Mod") ?? "";
            if (string.IsNullOrWhiteSpace(version)) continue;

            var date = Str(e, "Date");
            var started = DateTimeOffset.TryParse(date, out var d) ? d : DateTimeOffset.Now;

            // Accept both spellings. EvidenceOk is what Start-Gui.ps1 wrote after
            // the rename; AtlasOk is what older entries have.
            var evidence = Bool(e, "EvidenceOk") ?? Bool(e, "AtlasOk");
            var visual = Bool(e, "VisualOk") ?? false;

            var run = new RunRecord
            {
                Id = $"{started:yyyyMMdd-HHmmss}_{Sanitize(modIdFor)}_{Sanitize(version!)}_gui_imported",
                Started = started,
                Finished = started,
                ModId = modIdFor,
                ModDisplayName = modFolder,
                Variant = Str(e, "Edition") ?? "Default",
                ModFolder = modFolder,
                ModName = modFolder,
                ModVersion = Str(e, "ModVersion") ?? "",
                VersionId = version!,
                GameVersion = Str(e, "GameVersion") ?? "",
                Stage = TestStage.Gui,
                Status = RunStatus.Ok,
                StopReason = StopReason.Closed,
                LogPath = Str(e, "Log") ?? "",
                EvidenceOk = evidence,
                Visual = visual ? VisualState.Ok : VisualState.Failed,
                VisualAt = started,
                VisualNote = Str(e, "Note"),
                Note = I18n.Loc.T("store.importedNote"),
            };

            Save(run);
            imported.Add(run);
        }
        return imported;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool? Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : null;

    private static string Sanitize(string s) =>
        string.Concat(s.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '-'));
}
