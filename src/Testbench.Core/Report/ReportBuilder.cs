using System.Text;
using Testbench.Core.Config;
using Testbench.Core.I18n;
using Testbench.Core.Model;
using Testbench.Core.Store;

namespace Testbench.Core.Report;

public sealed class MatrixRow
{
    public string VersionId { get; set; } = "";
    public RunRecord? Headless { get; set; }
    public RunRecord? Gui { get; set; }

    /// <summary>Stage 1 passed.</summary>
    public bool HeadlessOk => Headless?.Status == RunStatus.Ok;

    /// <summary>Stage 2 passed AND a human confirmed it for the current mod version.</summary>
    public bool GuiOk { get; set; }

    /// <summary>Why GuiOk is false, in words that say what to do next.</summary>
    public string GuiNote { get; set; } = "";

    /// <summary>Only both stages together may put a version on a compatibility list.</summary>
    public bool FullPass => HeadlessOk && GuiOk;
}

public sealed class MatrixReport
{
    public string ModId { get; set; } = "";
    public string ModDisplayName { get; set; } = "";
    public string Variant { get; set; } = "";
    public string ModVersion { get; set; } = "";
    public DateTimeOffset Generated { get; set; } = DateTimeOffset.Now;
    public List<MatrixRow> Rows { get; set; } = new();

    public List<MatrixRow> Passed => Rows.Where(r => r.FullPass).ToList();
    public List<MatrixRow> PartialOnly => Rows.Where(r => r.HeadlessOk && !r.GuiOk).ToList();

    /// <summary>
    /// The line for README, the Nexus page and release.yml. Empty when nothing
    /// passed both stages, because an empty list is the honest answer then.
    /// </summary>
    public string TestedVersions
    {
        get
        {
            var v = Passed.Select(r => r.VersionId).ToList();
            return v.Count switch
            {
                0 => "",
                1 => v[0],
                _ => string.Join(", ", v.Take(v.Count - 1)) + I18n.Loc.T("report.listAnd") + v[^1],
            };
        }
    }
}

/// <summary>
/// Builds the compatibility matrix from stored runs.
/// </summary>
public static class ReportBuilder
{
    /// <summary>
    /// A version qualifies only if stage 1 is OK AND a stage 2 run for the CURRENT
    /// mod version confirms it.
    ///
    /// The binding to the mod version is deliberate: a release that touches DLL or
    /// atlas code invalidates every older visual check. The old matrix script had
    /// the right idea and matched on the game version alone, so a confirmation for
    /// a different mod could satisfy it.
    /// </summary>
    public static MatrixReport Build(
        ModConfig mod,
        string variant,
        string modVersion,
        RunStore store,
        IEnumerable<string> versions)
    {
        var report = new MatrixReport
        {
            ModId = mod.ModId,
            ModDisplayName = string.IsNullOrWhiteSpace(mod.DisplayName) ? mod.ModId : mod.DisplayName,
            Variant = variant,
            ModVersion = modVersion,
        };

        foreach (var v in versions)
        {
            var row = new MatrixRow
            {
                VersionId = v,
                Headless = store.LatestFor(mod.ModId, variant, v, TestStage.Headless),
                Gui = store.LatestFor(mod.ModId, variant, v, TestStage.Gui),
            };

            var gui = row.Gui;
            if (gui is null)
            {
                row.GuiNote = Loc.T("report.gui.noRun");
            }
            else if (!string.IsNullOrWhiteSpace(modVersion) &&
                     !string.Equals(gui.ModVersion, modVersion, StringComparison.OrdinalIgnoreCase))
            {
                row.GuiNote = Loc.T("report.gui.modVersionMismatch", gui.ModVersion, modVersion);
            }
            else if (gui.Status != RunStatus.Ok)
            {
                row.GuiNote = Loc.T("report.gui.status", gui.StatusText);
            }
            else if (gui.EvidenceOk == false)
            {
                var missing = gui.MissingEvidence.Count > 0 ? $" ({string.Join(", ", gui.MissingEvidence)})" : "";
                row.GuiNote = Loc.T("report.gui.evidenceMissing",
                    gui.EvidenceLabel ?? Loc.T("report.evidenceFallback"), missing);
            }
            else if (gui.Visual == VisualState.Pending)
            {
                row.GuiNote = Loc.T("report.gui.visualPending");
            }
            else if (gui.Visual != VisualState.Ok)
            {
                row.GuiNote = Loc.T("report.gui.visualNotConfirmed");
            }
            else
            {
                row.GuiOk = true;
            }

            report.Rows.Add(row);
        }

        return report;
    }

    public static string ToMarkdown(MatrixReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Loc.T("report.md.title", r.ModDisplayName, r.Variant, r.ModVersion,
            r.Generated.ToString("yyyy-MM-dd HH:mm")));
        sb.AppendLine();
        sb.AppendLine(Loc.T("report.md.tableHeader"));
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");

        var yes = Loc.T("report.yes");
        var no = Loc.T("report.no");

        foreach (var row in r.Rows)
        {
            var h = row.Headless;
            var cells = new[]
            {
                row.VersionId,
                h?.GameVersion ?? "",
                h?.StatusText ?? RunStatusText.Of(RunStatus.Untested),
                h is null ? "" : h.Analysis.ModLoaded ? yes : no,
                h is null ? "" : h.Analysis.HarmonyApplied ? yes : no,
                h?.Analysis.Errors.ToString() ?? "",
                h?.Analysis.Exceptions.ToString() ?? "",
                h?.Analysis.XmlProblems.ToString() ?? "",
                h?.Analysis.Ignored.ToString() ?? "",
                row.GuiOk ? RunStatusText.Of(RunStatus.Ok) : row.GuiNote,
            };
            sb.AppendLine("| " + string.Join(" | ", cells) + " |");
        }

        sb.AppendLine();
        sb.AppendLine(Loc.T("report.md.handover"));
        sb.AppendLine();
        sb.AppendLine(r.TestedVersions.Length > 0
            ? $"    TESTED_VERSIONS: \"{r.TestedVersions}\""
            : "    " + Loc.T("report.md.nothingPassed"));
        sb.AppendLine();

        if (r.PartialOnly.Count > 0)
        {
            sb.AppendLine(Loc.T("report.md.partialOnly",
                string.Join("; ", r.PartialOnly.Select(x => $"{x.VersionId} ({x.GuiNote})"))));
            sb.AppendLine();
        }

        sb.AppendLine(Loc.T("report.md.footer"));

        return sb.ToString();
    }

    public static string Write(MatrixReport report, MachineConfig machine)
    {
        Directory.CreateDirectory(machine.ResultRoot);
        var path = Path.Combine(machine.ResultRoot,
            $"matrix_{report.ModId}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.md");
        // StringBuilder.AppendLine already uses the platform newline; replacing
        // afterwards would turn every CRLF into CRCRLF.
        ConfigStore.WriteAtomic(path, ToMarkdown(report));
        return path;
    }
}
