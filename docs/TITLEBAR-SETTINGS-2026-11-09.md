# Correzioni title bar e Impostazioni — 9 novembre 2026

Base `33f5468dbb2d20f161efc0de54b6a5d8a424580e`, branch
`feature/engine-m6-consent-integrity`. Lavoro A/UX preesistente conservato.
Versione 1.1.5 e minimo Windows 10 17763 invariati. Nessuna dipendenza aggiunta.

## Causa e correzione della title bar

Il bridge UX rispondeva HTMAXBUTTON sul pulsante WPF, ma lasciava hover e
pressione alla gestione non-client predefinita. Il mouse non passava più al
normale Click WPF; Windows poteva tracciare/disegnare il proprio caption button
alle coordinate native, differenti dal controllo personalizzato. Questo spiega
il secondo quadrato e il comando incoerente. La regressione iniziale ha
dimostrato che WM_NCLBUTTONDOWN non veniva gestito dal bridge.

UseAeroCaptionButtons era già false: non era un secondo insieme dichiarato in
XAML. CaptionHeight=56, GlassFrameThickness=0, superficie opaca e WindowChrome
restano appropriati; non occorre tornare a un HWND layered né rimuovere i bit
nativi di minimizzazione/massimizzazione.

Ora il bridge possiede l'input NC del solo pulsante personalizzato: hover/leave,
pressione, capture e rilascio nelle coordinate corrette. Esegue un solo comando
di sistema se il rilascio è dentro il pulsante; drag fuori, annullamento e perdita
di capture non eseguono il comando. Stato e icona seguono WindowState come prima.
HTMAXBUTTON resta attivo per Snap su Windows 11. Windows 10 usa il normale input
WPF; il bridge specifico Windows 11 è protetto dal controllo della build 22000.

Fonti: [WM_NCLBUTTONDOWN e DefWindowProc](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-nclbuttondown),
[WM_NCMOUSEMOVE](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-ncmousemove),
[WindowChrome](https://learn.microsoft.com/en-us/dotnet/api/system.windows.shell.windowchrome),
[Snap Layouts](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-snap-layout-menu).
Le nuove API user32 (capture, coordinate, tracking) precedono Windows 10 1809.
Nessun attributo DWM o preferenza Windows viene forzato.

## Salvataggio delle preferenze

Prima: tema, testo, lingua e frequenza salvavano già; le sei checkbox cambiavano
solo l'oggetto e dipendevano da un successivo salvataggio globale/indiretto.
La visibilità dell'icona notifiche era applicata dal pulsante globale.

Ora si salvano al cambio:

- punto di ripristino per le future operazioni pertinenti;
- richiesta di installazione silenziosa;
- inclusione delle versioni installate non riconosciute;
- controllo automatico delle release di Update Center;
- scansione all'avvio;
- notifiche locali, con applicazione immediata della visibilità dell'icona;
- tema, dimensione del testo, lingua e frequenza, tramite lo stesso percorso.

Il Click commette il binding prima del salvataggio atomico. Le checkbox non
attivano azioni durante inizializzazione o rebinding. Nessun punto di ripristino,
scan, controllo release o installer viene avviato dal salvataggio. Restano nelle
proprie sezioni i comandi espliciti, come Gestisci spazio in Windows e Controlla
ora; i consensi delle operazioni future sono invariati.

Rimossi riquadro e pulsante globali, handler e traduzioni esclusive inutilizzate.
La descrizione IT/EN spiega l'automatismo. Se il disco rifiuta la scrittura, il
file precedente resta intatto: compare un errore localizzato con Riprova
salvataggio, esclusivamente durante l'errore. La descrizione chiarisce che le
modifiche restano attive solo nella sessione finché il salvataggio non riesce.
Nessun messaggio di successo viene mostrato in caso di errore.

## Verifiche

Host reale: Windows 11 build 26320, scala nativa corrente; nessuna modifica a
DPI, risoluzione, driver o preferenze Windows. Root dati GUID isolata prima di
qualsiasi I/O. Azioni di scansione/installazione rimosse dall'harness; soltanto
handler di preferenze e caption autorizzati nel test. Cursore ripristinato dopo
la prova hover. Nessuna sessione di cattura continua: screenshot puntuali e
render WPF, finestre QA chiuse al termine.

| Verifica | Esito |
| --- | --- |
| Build Release soluzione e harness | PASS, 0 errori/avvisi |
| Regressioni iniziali checkbox e input NC | RED osservati prima della correzione |
| Clic NC massimizza/ripristina, icona corretta, rilascio fuori/cancel | PASS |
| Comandi WPF equivalenti a tastiera/UIA, Minimizza/Ripristina e Chiudi | PASS |
| Hit test caption e bordo sinistro, limiti work area/taskbar | PASS |
| Un insieme di glyph e screenshot desktop in chiaro/scuro | PASS |
| Flyout Snap su hover reale | Osservato su Windows 11; una prova successiva ha registrato movimento concorrente del cursore |
| Sei checkbox, frequenza, tema/testo/lingua e icona notifiche | PASS |
| File bloccato realmente: errore IT/EN, vecchio file intatto, retry | PASS |
| Ricaricamento delle preferenze in processo separato | PASS |
| IT/EN, temi, scroll e viewport 720×500 senza barra di salvataggio | PASS |
| Patch notes/versione esatta e relativa UI/500 righe | PASS, nessuna modifica al codice note |
| Release audit e git diff --check | PASS |
| Windows 10 reale, DPI nativi 150/200%, cambio monitor | PENDING |
| Percezione delle animazioni native, tutte le scorciatoie e Narrator | PENDING manuale |

Comandi console: `--title-settings-ui`, `--settings-ui`, `--update-experience`,
`--update-experience-ui`, `--release-audit`. `--title-settings-reopen` è un helper
solo test che accetta esclusivamente una root temporanea GUID validata; non
avvia l'applicazione, scansioni o servizi. Nessun `dotnet test` usato come prova.

Evidenze: `_output/title-settings-20261109/`, inclusi `red.txt`, `ui-native.txt`,
`settings.txt`, `notes.txt`, `ux-ui.txt`, `audit.txt`, `solution-build.txt`,
`build.txt`, `native-title-Chiaro.png`, `native-title-Scuro.png` e immagini
`settings-{it,en}-{Chiaro,Scuro}.png`.

## File e Git

Incremento di questo intervento:

- `MainWindow.xaml`: click checkbox, descrizione, errore/retry, rimozione footer;
- `MainWindow.xaml.cs`: salvataggio comune, rimozione handler globale;
- `MainWindow.Settings.cs`: nuovo percorso autosave ed errori;
- `Services/NativeCaptionBridge.cs`: input NC del pulsante personalizzato;
- `Services/LocalizationService.cs`: descrizioni IT/EN e pulizia esclusiva;
- `Tests/UpdateCenter.SmokeTests/Program.cs`: dispatch isolati;
- `Tests/UpdateCenter.SmokeTests/SettingsUiTests.cs`: aspettative autosave;
- `Tests/UpdateCenter.SmokeTests/TitleSettingsUiTests.cs`: regressioni mirate;
- questo rapporto.

Indice vuoto, HEAD invariato, nessun commit/push/merge/tag/release. I file
condivisi contengono anche lavoro A/UX precedente: nessuno staging globale.
Gli snapshot `.snapshot` e `incremental-shared.patch` sotto `_output` separano
l'incremento dalla working tree iniziale. Gli altri file preesistenti sono stati
confrontati byte per byte e sono identici, compresi tutti i sorgenti patch notes.
Impostazioni di produzione verificate invariate tramite SHA256; stash invariato.
Motore WinGet/B2/M5/M6-B non modificato. Nessuna installazione, elevazione UAC o
download di payload.
