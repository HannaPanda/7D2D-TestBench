@{
    # ---------------------------------------------------------------
    #  Zentrale Konfiguration des 7DTD-Multiversion-Testbench.
    #  Eine neue Spielversion testen = Ordner anlegen + hier eintragen.
    # ---------------------------------------------------------------

    # Spielinstallationen liegen unter <GameRoot>\7DTD-<Version>
    GameRoot       = 'E:\Games'

    # Pro Version ein eigener UserDataFolder: <UserDataRoot>\<Version>
    # Bleibt zwischen Laeufen bestehen, damit die Navezgane-Welt nicht
    # jedes Mal neu erzeugt werden muss.
    UserDataRoot   = 'E:\Games\_TestUserData'

    # Quelle des zu testenden Mods
    ModRepo        = 'C:\Users\sourc\7D2D-Adamant'
    Editions       = @{
        Survival = 'AdamantBlock'
        Creative = 'AdamantBlock-Creative'
    }

    # Welche Versionen die Matrix abfaehrt. Reihenfolge = Reihenfolge im Report.
    Versions       = @('3.0.0', '3.0.1', '3.1.0')

    # Mods, die in der Testinstallation bleiben duerfen (alles andere wird
    # vor dem Lauf ausgeraeumt, damit ein Fehler eindeutig unserem Mod gehoert).
    # Der zu testende Mod und alles aus Dependencies kommen automatisch dazu.
    KeepMods       = @('0_TFP_Harmony')

    # Fremdmods, ohne die unsere Mods nicht vollstaendig laufen. Werden vor
    # JEDEM Lauf nach Mods\<Name> gespiegelt und nie ausgeraeumt.
    #
    # Warum das hier stehen muss und nicht "liegt halt in der Installation":
    # der Aufraeumschritt kennt nur KeepMods und schiebt alles andere nach
    # _Mods-deaktiviert. Ein einziger Smoketest hat so schon Gears und Quartz
    # aus allen drei Installationen verbannt - der naechste GUI-Test stand dann
    # ohne Settingsmenue da, ohne dass irgendwo etwas fehlgeschlagen waere.
    #
    # Gears braucht Quartz. Die Ladereihenfolge macht 7DTD ueber die
    # Ordnernamen ("0-", "00000-"), darum bleiben die Namen exakt so.
    Dependencies   = @(
        @{ Name = '0-Quartz'
           Source = 'C:\Modlists\Smorgasbord\mods\Quartz\0-Quartz' }
        @{ Name = '00000-Gears'
           Source = 'C:\Modlists\Smorgasbord\mods\Gears - A Mod Settings Manager\00000-Gears' }
    )

    # GamePrefs liegen in der Registry ausserhalb des UserDataFolders und
    # werden von ALLEN Installationen geteilt. Jeder Lauf sichert sie vorher
    # und spielt sie hinterher zurueck. Siehe README.md.
    PrefsKey       = 'HKCU\Software\The Fun Pimps\7 Days To Die'
    PrefsBackupDir = 'E:\Backup\7DTD-Prefs'

    # Abbruch, wenn der Server nicht innerhalb dieser Zeit hochkommt.
    TimeoutSeconds = 420

    # Logzeile, ab der der Start als abgeschlossen gilt - empirisch aus einem
    # vollen Headless-Startup ermittelt (3.0.0: bei 31,7 s).
    #
    # NICHT auf "Started Telnet" warten: das kommt schon nach ~2,7 s, lange
    # bevor die XMLs geladen sind, und wuerde jeden Lauf faelschlich gruen
    # melden. Bei einem neuen Build gegenpruefen, notfalls per -ReadyPattern
    # ueberschreiben.
    ReadyPattern   = 'INF StartGame done'

    # XML-Probleme. Die Muster stammen aus den echten Meldungstexten in
    # Assembly-CSharp (UTF-16-Bytesuche), nicht geraten:
    #   XML loader: Loading XML patch file '{0}' from mod '{1}' failed:
    #   XML patch for "{0}" from mod "{1}" did not apply: {2} (line {3} ...)
    #   XML.Patch ({0}, line {1} at pos {2}): Patch type ({3}) unknown
    #   XML loader: XML is missing: ...
    # Erfolgreiche Patches loggt 7DTD NICHT - "0 Treffer" heisst also
    # "nichts kaputt", nicht "geprueft". Das Muster ist per Negativkontrolle
    # verifiziert (absichtlich kaputter xpath -> wird erkannt).
    XmlProblemPattern = 'XML loader:|XML patch for .+ did not apply|XML\.Patch \(.+Patch type|No element <\w+> found!'

    # Bekanntes Rauschen, das bei JEDEM dedizierten Start auftritt und nichts
    # mit dem Mod zu tun hat. Bewusst eng gehalten - hier nur eintragen, was
    # nachweislich auch ohne Mod erscheint. Die Treffer werden nicht
    # verschluckt, sondern als "Ignoriert" mitgezaehlt und ausgewiesen.
    #   [EOS] DeviceId access credentials already exist ...  (Epic-Login)
    #   [Discord] / Retrieving remote news file ...          (Online-Dienste)
    IgnorePatterns = @(
        '\[EOS\]'
        '\[Discord\]'
        'Retrieving remote news file'
    )

    # Stufe 2 (Start-Gui.ps1). Alles hier ist mod-spezifisch und stand frueher
    # fest im Skript - dadurch fragte ein GUI-Lauf jedes anderen Mods nach
    # einem lila Kristallblock.
    #
    # EvidencePatterns: ALLE muessen im Log vorkommen, sonst gilt der Nachweis
    # als nicht erbracht. Weglassen heisst "fuer diesen Mod gibt es keinen im
    # Log belegbaren Nachweis" - dann bleibt nur die Sichtpruefung, und das
    # wird auch so ausgewiesen.
    Stage2         = @{
        LogFilter        = 'AdamantBlock|HarmonyException| EXC | ERR '
        EvidencePatterns = @('opaque atlas .*slice \d+ added', 'texture id \d+ applied')
        EvidenceLabel    = 'Atlas im Log belegt'
        VisualQuestion   = 'Sah der platzierte Block korrekt aus (lila-kristallin, nicht stahlgrau)?'
    }

    # Ergebnisse (Logs + Markdown-Report)
    ResultRoot     = 'E:\7DTD-Testbench\results'
}
