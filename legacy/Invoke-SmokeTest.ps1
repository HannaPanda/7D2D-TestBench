<#
.SYNOPSIS
    Headless-Smoketest eines Mods gegen EINE 7DTD-Version.

.DESCRIPTION
    Startet die Testinstallation als dedizierten Server (-batchmode -nographics),
    wartet bis die Welt geladen ist, beendet sie wieder und wertet das Log aus.

    Ohne GUI, ohne Steam-Overlay, ohne Klicken - dadurch skript- und
    schleifenfaehig ueber beliebig viele Versionen.

    Abgedeckt:  Mod-Laden, Harmony-Patches, XML-Parsing, Rezepte/Blocks/Material,
                Localization, alle ERR/EXC beim Start.
    NICHT abgedeckt: alles Grafische - insbesondere die Atlas-Textur-Injektion
                (TextureAtlasBlocks laeuft unter -nographics nicht) und das
                tatsaechliche Spielgefuehl. Dafuer bleibt ein GUI-Lauf noetig.

.EXAMPLE
    .\Invoke-SmokeTest.ps1 -Version 3.0.0
    .\Invoke-SmokeTest.ps1 -Version 3.1.0 -Edition Creative
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Version,
    [ValidateSet('Survival', 'Creative')][string] $Edition = 'Survival',
    [string] $ConfigPath = "$PSScriptRoot\Testbench.psd1",
    [int]    $TimeoutSeconds,
    # Marker, an dem der Start als abgeschlossen gilt. Ueberschreibbar, damit
    # ein neuer Build mit anderem Wortlaut nicht das Skript aendern muss.
    [string] $ReadyPattern
)

$ErrorActionPreference = 'Stop'

# reg.exe ist ein natives Programm: unter PowerShell 5.1 wird JEDE Ausgabe auf
# stderr zu einem NativeCommandError verpackt und reisst mit
# ErrorActionPreference=Stop das Skript um - auch bei Exit-Code 0. Darum hier
# gekapselt, ohne stderr-Redirect, mit Exit-Code-Pruefung.
function Invoke-Reg {
    param([Parameter(ValueFromRemainingArguments)][string[]] $RegArgs)
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try   { & reg.exe @RegArgs | Out-Null; return ($LASTEXITCODE -eq 0) }
    finally { $ErrorActionPreference = $prev }
}
$cfg = Import-PowerShellDataFile -Path $ConfigPath
if (-not $TimeoutSeconds) { $TimeoutSeconds = $cfg.TimeoutSeconds }

$game = Join-Path $cfg.GameRoot "7DTD-$Version"
$exe  = Join-Path $game '7DaysToDie.exe'
$udf  = Join-Path $cfg.UserDataRoot $Version
$mods = Join-Path $game 'Mods'
$src  = Join-Path $cfg.ModRepo $cfg.Editions[$Edition]

# Ordnername im Mods-Verzeichnis = Quellordner. Der Name, unter dem 7DTD den
# Mod dann meldet, ist ModInfo.xml's <Name> - bei allen bisherigen Mods gleich
# dem Ordner, aber nicht per Definition. Darum aus der ModInfo gelesen und nur
# als letzter Ausweg der Ordnername. Ohne das war der Test auf einen einzigen
# Mod verdrahtet und meldete jedem anderen "MOD NICHT GELADEN".
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
$modFolder = Split-Path $src -Leaf
$modName   = Get-ModName $src

# Fremdmods, die IMMER mitlaufen sollen (Gears/Quartz). Ohne sie ist ein Test
# der Settings-Integration nicht moeglich - und weil der Aufraeumschritt unten
# nur KeepMods kennt, wuerde er sie sonst bei jedem Lauf wegraeumen.
$deps = @()
$depProblems = @()
foreach ($d in @($cfg.Dependencies)) {
    if (-not $d -or -not $d.Name) { continue }
    # ModName wird erst NACH dem Bereitstellen aus der installierten Kopie
    # gelesen: der Ordner heisst '00000-Gears', geladen meldet sich der Mod als
    # 'Gears', und massgeblich ist, was tatsaechlich im Mods-Ordner liegt.
    $deps += [pscustomobject]@{ Name = $d.Name; Source = $d.Source; ModName = $null }
}

# ---- Ergebnisobjekt: wird immer zurueckgegeben, auch wenn etwas schiefgeht ----
$result = [ordered]@{
    Version = $Version; Edition = $Edition; Status = 'UNGETESTET'
    ModLoaded = $false; Harmony = $false; GameVersion = ''
    Deps = ''; Errors = 0; Exceptions = 0; XmlProblems = 0; Ignored = 0
    Log = ''; Note = ''
}
function Complete-Test([string]$status, [string]$note) {
    $result.Status = $status
    if ($note) { $result.Note = $note }
    [pscustomobject]$result
}

if (-not (Test-Path $exe))  { return Complete-Test 'FEHLT' "Keine Installation unter $game" }
if (-not (Test-Path $src))  { return Complete-Test 'FEHLER' "Mod-Quelle nicht gefunden: $src" }

$running = @(Get-Process -Name '7DaysToDie*' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    return Complete-Test 'FEHLER' "Es laeuft bereits eine 7DaysToDie.exe (PID $($running[0].Id))."
}

$stamp = Get-Date -Format 'yyyy-MM-dd_HH-mm-ss'
foreach ($d in @($udf, $cfg.PrefsBackupDir, $cfg.ResultRoot)) {
    New-Item -ItemType Directory -Force -Path $d | Out-Null
}
$log = Join-Path $cfg.ResultRoot "smoke_${Version}_${Edition}_$stamp.log"
$result.Log = $log

# ---- 1. GamePrefs sichern (geteilter Registry-Key, siehe README) ----
$prefsBak = Join-Path $cfg.PrefsBackupDir "prefs_pre_smoke_$stamp.reg"
Invoke-Reg export $cfg.PrefsKey $prefsBak /y | Out-Null
if (-not (Test-Path $prefsBak)) {
    return Complete-Test 'FEHLER' 'GamePrefs konnten nicht gesichert werden - Abbruch.'
}

try {
    # ---- 2. Mods-Ordner auf einen definierten Stand bringen ----
    # ⚠ Move-Item -Force ueberschreibt KEIN vorhandenes Verzeichnis, es scheitert
    # daran - und mit -ErrorAction SilentlyContinue lautlos. Ein Mod, der schon
    # einmal deaktiviert wurde, blieb dadurch bei jedem weiteren Lauf im
    # Mods-Ordner liegen und lud mit, ohne dass irgendwo etwas rot wurde.
    # Darum: Zielordner vorher raeumen (die Ablage ist per Definition Abfall)
    # und einen Fehlschlag melden statt verschlucken.
    $trash = Join-Path $game '_Mods-deaktiviert'
    New-Item -ItemType Directory -Force -Path $trash | Out-Null
    $keep = @($cfg.KeepMods) + @($modFolder) + @($deps.Name)
    foreach ($m in @(Get-ChildItem $mods -Directory -ErrorAction SilentlyContinue |
                     Where-Object { $_.Name -notin $keep })) {
        $to = Join-Path $trash $m.Name
        if (Test-Path $to) { Remove-Item $to -Recurse -Force }
        Move-Item $m.FullName $to -Force
        if (Test-Path $m.FullName) {
            throw "Konnte '$($m.Name)' nicht aus dem Mods-Ordner entfernen - der Lauf waere nicht aussagekraeftig."
        }
    }
    $dst = Join-Path $mods $modFolder
    if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
    & robocopy.exe $src $dst /E /NFL /NDL /NJH /NJS *> $null
    if ($LASTEXITCODE -ge 8) { throw "robocopy fehlgeschlagen (Exit $LASTEXITCODE)" }

    # ---- 2b. Abhaengigkeiten bereitstellen ----
    # Immer spiegeln, nicht nur wenn der Ordner fehlt: sonst haette man je nach
    # Vorgeschichte der Installation eine andere Gears-Version pro Version, und
    # genau die stillen Unterschiede soll ein Multiversionstest ausschliessen.
    # Deckt nebenbei den Fall ab, dass ein frueherer Lauf sie nach
    # _Mods-deaktiviert geschoben hat, bevor es diesen Schritt gab.
    foreach ($dep in $deps) {
        $depDst = Join-Path $mods $dep.Name
        if ($dep.Source -and (Test-Path $dep.Source)) {
            if (Test-Path $depDst) { Remove-Item $depDst -Recurse -Force }
            & robocopy.exe $dep.Source $depDst /E /NFL /NDL /NJH /NJS *> $null
            if ($LASTEXITCODE -ge 8) { throw "robocopy der Abhaengigkeit '$($dep.Name)' fehlgeschlagen (Exit $LASTEXITCODE)" }
        }
        else {
            # Quelle unerreichbar. Eine vorhandene aeltere Kopie wird NICHT
            # geloescht - dann laeuft der Test wenigstens mit dem, was da ist -
            # aber der Lauf muss das ausweisen, statt es als frisch auszugeben.
            $depProblems += "$($dep.Name): Quelle fehlt"
            Write-Warning "Abhaengigkeit '$($dep.Name)' nicht unter '$($dep.Source)'."
        }
        # Immer aus der INSTALLIERTEN Kopie lesen, nicht aus der Quelle: nur die
        # sagt, unter welchem Namen sich der Mod gleich im Log melden wird.
        if (Test-Path $depDst) { $dep.ModName = Get-ModName $depDst }
    }

    # ---- 3. Headless starten ----
    # Argumente wie in TFPs startdedicated.bat, plus unsere Isolation.
    $gameArgs = @(
        '-logfile', $log
        '-quit', '-batchmode', '-nographics'
        '-configfile=serverconfig.xml'
        "-UserDataFolder=$udf"
        '-noeac'
        '-dedicated'
    )
    Write-Host "[$Version/$Edition] starte headless ..." -ForegroundColor Cyan
    $proc = Start-Process -FilePath $exe -ArgumentList $gameArgs -WorkingDirectory $game -PassThru

    # ---- 4. Auf Startabschluss ODER Fatal ODER Timeout warten ----
    # Default absichtlich SPAET: "Started Telnet" kommt schon nach ~3s, lange
    # bevor die XMLs geladen sind - darauf zu warten testet praktisch nichts.
    $ready = if ($ReadyPattern) { $ReadyPattern } else { $cfg.ReadyPattern }
    $fatal = '(?i)(Fatal error|System\.(NullReference|Type|Missing|Argument)\w*Exception|HarmonyException)'
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $hit = ''
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 3
        if (Test-Path $log) {
            $txt = Get-Content $log -Raw -ErrorAction SilentlyContinue
            if ($txt -match $ready) { $hit = 'ready'; break }
            if ($txt -match $fatal) { $hit = 'fatal'; break }
        }
        if ($proc.HasExited) { $hit = 'exited'; break }
    }
    if (-not $hit) { $hit = 'timeout' }

    # ---- 5. Beenden und warten, bis der Prozess wirklich weg ist ----
    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    while (Get-Process -Name '7DaysToDie*' -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 1 }
    Start-Sleep -Seconds 2
}
finally {
    # ---- 6. GamePrefs IMMER zurueckspielen, auch nach einem Fehler ----
    if (-not (Invoke-Reg import $prefsBak)) {
        Write-Warning "GamePrefs-Restore fehlgeschlagen. Manuell: reg import `"$prefsBak`""
    }
}

# ---- 7. Log auswerten ----
if (-not (Test-Path $log)) { return Complete-Test 'FEHLER' 'Kein Logfile entstanden.' }
$lines = Get-Content $log -ErrorAction SilentlyContinue

$result.ModLoaded = [bool]($lines -match ('\[MODS\]\s+Loaded Mod: ' + [regex]::Escape($modName)))
$result.Harmony   = [bool]($lines -match 'Harmony patches applied')

# Bereitgestellt heisst nicht geladen. Eine Abhaengigkeit, die stillschweigend
# nicht hochkommt, macht jeden Test ihrer Integration wertlos - also wird sie
# im Log nachgewiesen und nicht bloss kopiert.
foreach ($dep in $deps) {
    if (-not $dep.ModName) { $depProblems += "$($dep.Name): nicht installiert"; continue }
    if ($lines -match ('\[MODS\]\s+Loaded Mod: ' + [regex]::Escape($dep.ModName))) { continue }
    $depProblems += "$($dep.Name): nicht geladen"
}
$result.Deps = if ($depProblems.Count -gt 0) { $depProblems -join '; ' }
               else { ($deps | ForEach-Object { "$($_.Name) ($($_.ModName))" }) -join ', ' }

# Bekanntes Start-Rauschen rausrechnen, aber sichtbar lassen: was ignoriert
# wurde, wird gezaehlt und ausgewiesen - stilles Wegfiltern waere genau die
# Sorte gruener Haken, die nichts bedeutet.
$noise    = if ($cfg.IgnorePatterns) { ($cfg.IgnorePatterns -join '|') } else { $null }
$relevant = if ($noise) { $lines | Where-Object { $_ -notmatch $noise } } else { $lines }
$result.Ignored = $lines.Count - @($relevant).Count

$result.Errors      = @($relevant -match ' ERR ').Count
$result.Exceptions  = @($relevant -match ' EXC |Exception:').Count
$xmlPat = if ($cfg.XmlProblemPattern) { $cfg.XmlProblemPattern } else { 'XML loader:' }
$result.XmlProblems = @($relevant -match $xmlPat).Count
# Achtung: -match auf einem ARRAY filtert nur und fuellt $Matches nicht.
# Darum erst die Zeile ziehen, dann einzeln matchen.
$verLine = $lines | Where-Object { $_ -match 'INF Version: V [\d.]+' } | Select-Object -First 1
if ($verLine -and $verLine -match 'INF Version: (V [\d.]+)') {
    $result.GameVersion = $Matches[1]
}

$status = if     ($hit -eq 'fatal')                     { 'FATAL'   }
          elseif (-not $result.ModLoaded)               { 'MOD NICHT GELADEN' }
          elseif ($depProblems.Count -gt 0)             { 'ABHAENGIGKEIT FEHLT' }
          elseif (-not $result.Harmony)                 { 'HARMONY FEHLT' }
          elseif ($result.Exceptions -gt 0)             { 'EXCEPTIONS' }
          elseif ($result.Errors -gt 0)                 { 'ERRORS' }
          elseif ($result.XmlProblems -gt 0)            { 'XML-WARNUNGEN' }
          elseif ($hit -eq 'timeout')                   { 'TIMEOUT' }
          else                                          { 'OK' }

Complete-Test $status "Abbruchgrund: $hit"
