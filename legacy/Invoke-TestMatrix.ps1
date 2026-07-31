<#
.SYNOPSIS
    Faehrt den Headless-Smoketest ueber alle konfigurierten Spielversionen.

.DESCRIPTION
    Ergebnis: eine Tabelle auf der Konsole plus ein Markdown-Report unter
    <ResultRoot>\matrix_<zeitstempel>.md, dessen Kompatibilitaetszeile sich
    direkt in README / Nexus / release.yml uebernehmen laesst.

    Es wird IMMER nur eine Version gleichzeitig getestet - die Instanzen teilen
    sich Steam, den Registry-Prefs-Key und die Server-Ports, parallel geht also
    nicht.

.EXAMPLE
    .\Invoke-TestMatrix.ps1
    .\Invoke-TestMatrix.ps1 -Versions 3.0.0,3.0.1,3.1.0 -Edition Survival
#>
[CmdletBinding()]
param(
    [string[]] $Versions,
    [ValidateSet('Survival', 'Creative')][string] $Edition = 'Survival',
    [string]   $ConfigPath = "$PSScriptRoot\Testbench.psd1"
)

$ErrorActionPreference = 'Stop'
$cfg = Import-PowerShellDataFile -Path $ConfigPath
if (-not $Versions) { $Versions = $cfg.Versions }

$results = foreach ($v in $Versions) {
    & "$PSScriptRoot\Invoke-SmokeTest.ps1" -Version $v -Edition $Edition -ConfigPath $ConfigPath
}

# ---- Stufe 2 dazuholen -------------------------------------------------
# Der Headless-Test kann die Atlas-Injektion nicht sehen. Eine Version darf
# darum nur dann als kompatibel vorgeschlagen werden, wenn ZUSAETZLICH ein
# GUI-Lauf vorliegt - und zwar fuer die AKTUELLE Mod-Version, sonst schleppt
# man Bestaetigungen ueber ein Release hinweg mit.
$modVer = ''
$mi = Join-Path (Join-Path $cfg.ModRepo $cfg.Editions[$Edition]) 'ModInfo.xml'
if (Test-Path $mi) { $modVer = ([xml](Get-Content $mi -Raw)).xml.Version.value }

$store = Join-Path $PSScriptRoot 'gui-verified.json'
# ACHTUNG PowerShell 5.1: ConvertFrom-Json gibt ein JSON-Array als EIN Objekt
# in die Pipeline. @(...) entpackt das nicht, sondern verpackt es zusaetzlich
# (Count=1, [0] ist selbst ein Array). [object[]] castet korrekt.
$gui = if (Test-Path $store) { [object[]](Get-Content $store -Raw | ConvertFrom-Json) } else { @() }

foreach ($r in $results) {
    $g = $gui | Where-Object { $_.Version -eq $r.Version } | Select-Object -First 1
    $ok = $g -and $g.AtlasOk -and $g.VisualOk -and ($g.ModVersion -eq $modVer)
    Add-Member -InputObject $r -NotePropertyName 'GuiOk' -NotePropertyValue ([bool]$ok) -Force
    $why = if (-not $g)                          { 'kein GUI-Lauf' }
           elseif ($g.ModVersion -ne $modVer)    { "GUI-Lauf war Mod $($g.ModVersion), aktuell $modVer" }
           elseif (-not $g.AtlasOk)              { 'Atlas im Log nicht belegt' }
           elseif (-not $g.VisualOk)             { 'Sichtpruefung nicht bestaetigt' }
           else                                  { '' }
    Add-Member -InputObject $r -NotePropertyName 'GuiNote' -NotePropertyValue $why -Force
}

Write-Host ''
$results | Format-Table Version, GameVersion, Edition, Status, ModLoaded, Harmony, Errors, Exceptions, XmlProblems, GuiOk, GuiNote -AutoSize

# ---- Markdown-Report ----
$stamp  = Get-Date -Format 'yyyy-MM-dd_HH-mm-ss'
$report = Join-Path $cfg.ResultRoot "matrix_$stamp.md"
# Nur was BEIDE Stufen bestanden hat, darf vorgeschlagen werden.
$passed  = @($results | Where-Object { $_.Status -eq 'OK' -and $_.GuiOk })
$partial = @($results | Where-Object { $_.Status -eq 'OK' -and -not $_.GuiOk })

$md = @()
$md += "# Kompatibilitaets-Matrix - $Edition $modVer - $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
$md += ''
$md += '| Version | Gemeldet | Headless | Mod geladen | Harmony | ERR | EXC | XML | GUI |'
$md += '|---|---|---|---|---|---|---|---|---|'
foreach ($r in $results) {
    $md += "| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} |" -f `
        $r.Version, $r.GameVersion, $r.Status, $r.ModLoaded, $r.Harmony,
        $r.Errors, $r.Exceptions, $r.XmlProblems,
        $(if ($r.GuiOk) { 'OK' } else { $r.GuiNote })
}
$md += ''
$md += '## Uebernahme in README / Nexus / release.yml'
$md += ''
if ($passed.Count -gt 0) {
    $list = if ($passed.Count -eq 1) { $passed[0].Version }
            else { (@($passed[0..($passed.Count-2)].Version) -join ', ') + ' and ' + $passed[-1].Version }
    $md += "    TESTED_VERSIONS: `"$list`""
} else {
    $md += '    (keine Version hat BEIDE Stufen bestanden - nichts als kompatibel melden)'
}
$md += ''
if ($partial.Count -gt 0) {
    $md += ('**Nur Stufe 1 bestanden, NICHT vorschlagen:** ' + (@($partial | ForEach-Object { "$($_.Version) ($($_.GuiNote))" }) -join '; ') + '.')
    $md += ''
}
$md += '**Headless deckt nichts Grafisches ab.** Die Atlas-/Textur-Injektion laeuft'
$md += 'unter `-nographics` nicht mit. Eine Version kommt erst auf die Liste, wenn'
$md += 'zusaetzlich `Start-Gui.ps1 -Version <v>` gelaufen ist, dort die Atlas-Zeilen'
$md += 'im Log stehen UND die Sichtpruefung des platzierten Blocks bestaetigt wurde.'
$md += 'Die Bestaetigung ist an die Mod-Version gebunden und verfaellt beim naechsten'
$md += 'Release.'

$md -join "`r`n" | Set-Content -Path $report -Encoding UTF8
Write-Host "Report: $report" -ForegroundColor Cyan
$results
