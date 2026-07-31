using System.Collections.ObjectModel;
using System.IO;
using Testbench.Core.Config;

namespace Testbench.Gui;

/// <summary>One found installation, as a row with a checkbox and an editable id.</summary>
public sealed class CandidateRow : Notifier
{
    private bool _take;
    private string _id;

    public CandidateRow(VersionCandidate candidate)
    {
        Candidate = candidate;
        _id = candidate.ProposedId ?? "";
        // Preselected only where nothing is in doubt. A contradiction between
        // folder name and build has to be looked at, not clicked away.
        _take = candidate is { HasExe: true, Registered: false, Mismatch: false } && _id.Length > 0;
    }

    public VersionCandidate Candidate { get; }

    public string Dir => Candidate.Dir;
    public string Info => Candidate.Explain();
    public bool CanTake => Candidate is { HasExe: true, Registered: false };
    public bool Warn => Candidate.Mismatch || Candidate.Source == IdSource.FolderName || !Candidate.HasHarmony;

    public string Id
    {
        get => _id;
        set => Set(ref _id, value.Trim());
    }

    public bool Take
    {
        get => _take && CanTake;
        set => Set(ref _take, value);
    }
}

/// <summary>A version already in the machine configuration.</summary>
public sealed class RegisteredRow
{
    public RegisteredRow(MachineConfig machine, GameVersion version)
    {
        Version = version;
        Dir = machine.GameDir(version.Id);
        Installed = File.Exists(Path.Combine(Dir, VersionScanner.ExeName));

        var live = Installed ? VersionScanner.ReadBuild(Dir) : null;
        Info = !Installed
            ? "keine 7DaysToDie.exe - so kann kein Lauf stattfinden"
            : version.Build is not null && live is not null && version.Build != live
                ? $"Build hat sich geaendert: eingetragen {version.Build}, dort liegt {live}"
                : $"Build {live ?? version.Build ?? "unbekannt"}";
    }

    public GameVersion Version { get; }
    public string Id => Version.Id;
    public string Dir { get; }
    public bool Installed { get; }
    public string Info { get; }
}

/// <summary>
/// State of the version dialog: what is registered, what is lying on disk, and
/// what would be added.
///
/// The point of this window is that a new game version does not need a typed
/// command any more. The version is read out of the installation itself
/// (MicrosoftGame.Config), the folder name is only the fallback, and where the
/// two disagree the row says so instead of quietly choosing one.
/// </summary>
public sealed class VersionsViewModel : Notifier
{
    private readonly MachineConfig _machine;
    private readonly string _machinePath;
    private string _root;
    private string _status = "";

    public VersionsViewModel(MachineConfig machine, string machinePath)
    {
        _machine = machine;
        _machinePath = machinePath;
        _root = machine.GameRoot;
        RefreshRegistered();
    }

    public ObservableCollection<CandidateRow> Candidates { get; } = new();
    public ObservableCollection<RegisteredRow> Registered { get; } = new();

    /// <summary>Set when the configuration was written, so the main window reloads.</summary>
    public bool Changed { get; private set; }

    public string Root
    {
        get => _root;
        set => Set(ref _root, value);
    }

    public string Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    /// <summary>Searches a folder tree for installations.</summary>
    public void Scan()
    {
        Candidates.Clear();
        if (!Directory.Exists(Root))
        {
            Status = $"Ordner fehlt: {Root}";
            return;
        }

        var found = VersionScanner.Scan(Root, _machine);
        foreach (var c in found) Candidates.Add(new CandidateRow(c));

        var neu = Candidates.Count(c => c.CanTake);
        Status = found.Count == 0
            ? $"In {Root} keine Installation gefunden."
            : $"{found.Count} Installation(en) in {Root}, davon {neu} nicht eingetragen.";
    }

    /// <summary>Takes a single folder the user picked, wherever it lies.</summary>
    public void AddFolder(string dir)
    {
        if (!Directory.Exists(dir)) { Status = $"Ordner fehlt: {dir}"; return; }

        var candidate = VersionScanner.Inspect(dir, _machine);
        if (!candidate.HasExe)
        {
            // Choosing E:\Games instead of one installation is the obvious slip,
            // so it is worth answering usefully instead of refusing.
            var inside = VersionScanner.Scan(dir, _machine);
            if (inside.Count > 0)
            {
                Root = dir;
                Scan();
                return;
            }
            Status = $"In {dir} liegt keine {VersionScanner.ExeName}.";
            return;
        }

        var existing = Candidates.FirstOrDefault(c => ConfigStore.PathsEqual(c.Dir, candidate.Dir));
        if (existing is not null)
        {
            existing.Take = existing.CanTake;
            Status = $"{candidate.Dir} war schon in der Liste.";
            return;
        }

        Candidates.Insert(0, new CandidateRow(candidate));
        Status = candidate.Registered
            ? $"{candidate.Dir} ist schon als {candidate.RegisteredAs} eingetragen."
            : $"Erkannt: {candidate.ProposedId ?? "keine Version erkennbar"} ({candidate.Explain()})";
    }

    /// <summary>Writes the checked rows into the machine configuration.</summary>
    public void Apply()
    {
        var take = Candidates.Where(c => c.Take).ToList();
        if (take.Count == 0) { Status = "Nichts ausgewaehlt."; return; }

        var done = new List<string>();
        foreach (var row in take)
        {
            if (row.Id.Length == 0) { Status = $"{row.Dir}: keine Version angegeben."; return; }
            if (_machine.FindVersion(row.Id) is not null)
            {
                Status = $"'{row.Id}' ist schon eingetragen, mit einem anderen Ordner. Erst dort entfernen.";
                return;
            }

            var isDefaultDir = ConfigStore.PathsEqual(row.Dir, Path.Combine(_machine.GameRoot, $"7DTD-{row.Id}"));
            _machine.Versions.Add(new GameVersion
            {
                Id = row.Id,
                Path = isDefaultDir ? null : row.Dir,
                Build = row.Candidate.Build,
                Branch = $"v{row.Id}",
            });
            done.Add(row.Id);
        }

        // Builds of versions that were already registered get written down too,
        // otherwise the drift check has nothing to compare against later.
        foreach (var c in Candidates.Where(c => c.Candidate.Registered && c.Candidate.Build is not null))
        {
            var entry = _machine.FindVersion(c.Candidate.RegisteredAs!);
            if (entry is not null && entry.Build is null) entry.Build = c.Candidate.Build;
        }

        Save();
        Status = $"Eingetragen: {string.Join(", ", done)}.";
        Scan();
    }

    /// <summary>
    /// Removes a version from the configuration. The installation stays where it
    /// is: this tool never deletes a game.
    /// </summary>
    public void Remove(RegisteredRow row)
    {
        var entry = _machine.FindVersion(row.Id);
        if (entry is null) return;
        _machine.Versions.Remove(entry);
        Save();
        Status = $"'{row.Id}' entfernt. Der Ordner {row.Dir} bleibt liegen.";
        Scan();
    }

    private void Save()
    {
        _machine.Versions.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase));
        ConfigStore.SaveMachine(_machine, _machinePath);
        Changed = true;
        RefreshRegistered();
    }

    private void RefreshRegistered()
    {
        Registered.Clear();
        foreach (var v in _machine.Versions) Registered.Add(new RegisteredRow(_machine, v));
    }
}
