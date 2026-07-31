<#
.SYNOPSIS
    GUI-Lauf einer beliebigen Testversion - mit GamePrefs-Schutz.

.DESCRIPTION
    Stufe 2 des Testbench. Noetig fuer alles, was der Headless-Smoketest
    NICHT abdeckt - vor allem die Atlas-/Textur-Injektion, die unter
    -nographics gar nicht erst laeuft.

    Loest die versionsspezifische Start-Test.bat ab: eine Datei fuer alle
    Versionen statt eine pro Installation.

.EXAMPLE
    .\Start-Gui.ps1 -Version 3.1.0
    .\Start-Gui.ps1 -Version 3.0.0 -Edition Survival
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Version,
    [ValidateSet('Survival', 'Creative')][string] $Edition = 'Creative',
    [switch] $SkipDeploy,
    # Sichtpruefung ohne Rueckfrage als bestanden verbuchen (fuer Skripte).
    # Ohne das fragt das Skript am Ende nach - was das Auge geprueft hat,
    # kann kein Logmuster ersetzen.
    [switch] $ConfirmVisual,
    [string] $ConfigPath = "$PSScriptRoot\Testbench.psd1"
)

$ErrorActionPreference = 'Stop'
$cfg = Import-PowerShellDataFile -Path $ConfigPath

# Siehe Invoke-SmokeTest.ps1: native Exes duerfen unter PS 5.1 keinen
# stderr-Redirect abbekommen, sonst reisst NativeCommandError das Skript um.
function Invoke-Reg {
    param([Parameter(ValueFromRemainingArguments)][string[]] $RegArgs)
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try   { & reg.exe @RegArgs | Out-Null; return ($LASTEXITCODE -eq 0) }
    finally { $ErrorActionPreference = $prev }
}

# Wie im Smoketest: Ordnername = Quellordner, gemeldeter Name = ModInfo <Name>.
function Get-ModName([string]$Folder) {
    $info = Join-Path $Folder 'ModInfo.xml'
    if (Test-Path $info) {
        try {
            $n = ([xml](Get-Content $info -Raw)).xml.Name.value
            if ($n) { return $n }
        } catch { }
    }
    return (Split-Path $Folder -Leaf)
}

$game = Join-Path $cfg.GameRoot "7DTD-$Version"
$exe  = Join-Path $game '7DaysToDie.exe'
$src       = Join-Path $cfg.ModRepo $cfg.Editions[$Edition]
$modFolder = Split-Path $src -Leaf
$stage2    = if ($cfg.Stage2) { $cfg.Stage2 } else { @{} }
$udf  = Join-Path $cfg.UserDataRoot "$Version-gui"
$log  = Join-Path $udf "logs\gui_$(Get-Date -Format 'yyyy-MM-dd_HH-mm-ss').log"

if (-not (Test-Path $exe)) { throw "Keine Installation unter $game" }
if ($udf -eq (Join-Path $env:APPDATA '7DaysToDie')) { throw 'UserDataFolder zeigt auf die LIVE-Daten.' }
$running = @(Get-Process -Name '7DaysToDie*' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) { throw "Es laeuft bereits eine 7DaysToDie.exe (PID $($running[0].Id))." }
if (-not (Get-Process -Name 'steam' -ErrorAction SilentlyContinue)) {
    Write-Warning 'Steam laeuft nicht - das Spiel braucht den laufenden Client.'
}

New-Item -ItemType Directory -Force -Path (Split-Path $log), $cfg.PrefsBackupDir | Out-Null

if (-not $SkipDeploy) {
    # Aufraeumen wie im Smoketest. Ohne das laedt der GUI-Lauf noch mit, was
    # der letzte Smoketest zufaellig dagelassen hat - man testet dann seinen
    # Mod im Beisein eines fremden, ohne es zu merken.
    # ⚠ Move-Item -Force ueberschreibt kein vorhandenes Verzeichnis, sondern
    # scheitert daran; der Zielordner wird darum vorher geraeumt.
    $trash = Join-Path $game '_Mods-deaktiviert'
    New-Item -ItemType Directory -Force -Path $trash | Out-Null
    $keep = @($cfg.KeepMods) + @($modFolder) + @(@($cfg.Dependencies) | ForEach-Object { $_.Name })
    foreach ($m in @(Get-ChildItem (Join-Path $game 'Mods') -Directory -ErrorAction SilentlyContinue |
                     Where-Object { $_.Name -notin $keep })) {
        $to = Join-Path $trash $m.Name
        if (Test-Path $to) { Remove-Item $to -Recurse -Force }
        Move-Item $m.FullName $to -Force
        if (Test-Path $m.FullName) { throw "Konnte '$($m.Name)' nicht aus dem Mods-Ordner entfernen." }
        Write-Host "Deaktiviert: $($m.Name)" -ForegroundColor DarkGray
    }

    $dst = Join-Path $game "Mods\$modFolder"
    if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
    & robocopy.exe $src $dst /E /NFL /NDL /NJH /NJS | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy fehlgeschlagen (Exit $LASTEXITCODE)" }
    Write-Host "Deployed: $Edition -> $dst" -ForegroundColor Cyan

    # ---- Abhaengigkeiten (Gears/Quartz) ----
    # Der GUI-Lauf ist genau der, in dem ein fehlendes Settingsmenue weh tut,
    # und der Smoketest raeumt sie aus derselben Installation weg, wenn sie
    # nicht in Dependencies stehen. Also hier genauso bereitstellen.
    foreach ($dep in @($cfg.Dependencies)) {
        if (-not $dep -or -not $dep.Name) { continue }
        if (-not $dep.Source -or -not (Test-Path $dep.Source)) {
            Write-Warning "Abhaengigkeit '$($dep.Name)' nicht unter '$($dep.Source)' - der Lauf startet ohne sie."
            continue
        }
        $depDst = Join-Path $game "Mods\$($dep.Name)"
        if (Test-Path $depDst) { Remove-Item $depDst -Recurse -Force }
        & robocopy.exe $dep.Source $depDst /E /NFL /NDL /NJH /NJS | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "robocopy der Abhaengigkeit '$($dep.Name)' fehlgeschlagen (Exit $LASTEXITCODE)" }
        Write-Host "Deployed: $($dep.Name) -> $depDst" -ForegroundColor DarkCyan
    }
}

# GamePrefs liegen in HKCU und werden von allen Installationen geteilt -
# ohne Sicherung ueberschreibt dieser Lauf die getunten Live-Settings.
$bak = Join-Path $cfg.PrefsBackupDir "prefs_pre_gui_$(Get-Date -Format 'yyyy-MM-dd_HH-mm-ss').reg"
if (-not (Invoke-Reg export $cfg.PrefsKey $bak /y) -or -not (Test-Path $bak)) {
    throw 'GamePrefs konnten nicht gesichert werden - Abbruch.'
}

Write-Host "Start 7DTD $Version (GUI, ohne EAC). Log: $log" -ForegroundColor Cyan
try {
    $proc = Start-Process -FilePath $exe -WorkingDirectory $game -PassThru -ArgumentList @(
        "-UserDataFolder=$udf", '-logfile', $log, '-noeac'
    )
    $proc.WaitForExit()
    while (Get-Process -Name '7DaysToDie*' -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 1 }
    Start-Sleep -Seconds 2
}
finally {
    if (Invoke-Reg import $bak) { Write-Host 'GamePrefs wiederhergestellt.' -ForegroundColor Green }
    else { Write-Warning "Restore fehlgeschlagen. Manuell: reg import `"$bak`"" }
}

Write-Host ''
Write-Host 'Interessante Zeilen aus dem Lauf:' -ForegroundColor Cyan
$lines = Get-Content $log -ErrorAction SilentlyContinue
$filter = if ($stage2.LogFilter) { $stage2.LogFilter }
          else { [regex]::Escape($modFolder) + '|HarmonyException| EXC | ERR ' }
$lines | Select-String -Pattern $filter | Select-Object -First 20 | ForEach-Object { $_.Line }

foreach ($dep in @($cfg.Dependencies)) {
    if (-not $dep -or -not $dep.Name) { continue }
    $depDir = Join-Path $game "Mods\$($dep.Name)"
    $depName = if (Test-Path $depDir) { Get-ModName $depDir } else { $dep.Name }
    $loaded = [bool]($lines -match ('\[MODS\]\s+Loaded Mod: ' + [regex]::Escape($depName)))
    Write-Host ("Abhaengigkeit {0} ({1}): {2}" -f $dep.Name, $depName, $(if ($loaded) { 'geladen' } else { 'NICHT GELADEN' })) `
        -ForegroundColor $(if ($loaded) { 'Green' } else { 'Red' })
}

# ---- Stufe-2-Nachweis festhalten --------------------------------------
# Zwei getrennte Dinge, die nicht vermischt werden duerfen:
#   evidenceOk - im Log belegbar (mod-spezifische Muster aus Stage2)
#   visualOk   - nur ein Mensch kann sagen, ob es richtig AUSSIEHT/sich richtig anfuehlt
# Ohne EvidencePatterns gibt es fuer den Mod keinen im Log belegbaren Nachweis;
# dann bleibt nur die Sichtpruefung, und das wird auch so ausgewiesen statt
# ein leeres Muster als "bestanden" zu verbuchen.
$evidencePatterns = @($stage2.EvidencePatterns) | Where-Object { $_ }
$evidenceOk = $null
if ($evidencePatterns.Count -gt 0) {
    $evidenceOk = $true
    foreach ($pat in $evidencePatterns) {
        if (-not (@($lines -match $pat).Count)) { $evidenceOk = $false }
    }
}
$gameVer = ''
$vl = $lines | Where-Object { $_ -match 'INF Version: V [\d.]+' } | Select-Object -First 1
if ($vl -and $vl -match 'INF Version: (V [\d.]+[^,]*)') { $gameVer = $Matches[1].Trim() }

$modVer = ''
$mi = Join-Path $game "Mods\$modFolder\ModInfo.xml"
if (Test-Path $mi) { $modVer = ([xml](Get-Content $mi -Raw)).xml.Version.value }

$evidenceLabel = if ($stage2.EvidenceLabel) { $stage2.EvidenceLabel } else { 'Im Log belegt' }
Write-Host ''
if ($null -eq $evidenceOk) {
    Write-Host "${evidenceLabel}: kein Logmuster konfiguriert - nur Sichtpruefung" -ForegroundColor Yellow
} else {
    Write-Host ("{0}: {1}" -f $evidenceLabel, $(if ($evidenceOk) { 'JA' } else { 'NEIN' })) `
        -ForegroundColor $(if ($evidenceOk) { 'Green' } else { 'Red' })
}

$question = if ($stage2.VisualQuestion) { $stage2.VisualQuestion } else { 'Sah/verhielt sich alles wie erwartet?' }
$visualOk = $false
if ($ConfirmVisual) { $visualOk = $true }
else {
    $a = Read-Host "$question [j/N]"
    $visualOk = ($a -match '^(j|y)')
}

$store = Join-Path $PSScriptRoot 'gui-verified.json'
$all = if (Test-Path $store) { Get-Content $store -Raw | ConvertFrom-Json } else { @() }
# Pro Mod+Version+Edition genau ein Eintrag - sonst ueberschriebe der Nachweis
# eines Mods den eines anderen fuer dieselbe Spielversion.
$all = @($all | Where-Object {
    -not ($_.Version -eq $Version -and $_.Edition -eq $Edition -and $_.Mod -eq $modFolder)
})
$all += [pscustomobject]@{
    Version = $Version; Edition = $Edition; Mod = $modFolder; ModVersion = $modVer
    GameVersion = $gameVer; EvidenceOk = $evidenceOk; VisualOk = $visualOk
    Date = (Get-Date -Format 'yyyy-MM-dd HH:mm'); Log = $log
}
$all | ConvertTo-Json -Depth 4 | Set-Content $store -Encoding UTF8
Write-Host "Stufe-2-Nachweis gespeichert: $store" -ForegroundColor Cyan
