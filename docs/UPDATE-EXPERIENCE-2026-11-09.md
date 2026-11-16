# Esperienza aggiornamenti e note di rilascio — 9 novembre 2026

Baseline `33f5468dbb2d20f161efc0de54b6a5d8a424580e`, branch
`feature/engine-m6-consent-integrity`. Versione 1.1.5 e target Windows 10
17763 invariati. Nessun installer, payload, elevazione o modifica delle
impostazioni schermo. Nessuna nuova dipendenza.

## Finestra

La finestra principale era layered (`AllowsTransparency=True`) e priva del
frame non-client (`WindowStyle=None`). Il resize manuale ricostruiva soltanto
gli hit test dei bordi. Ora usa HWND opaco, WindowChrome, caption 56 DIP e
resize 8 DIP, con comandi di sistema. Rimossi drag e resize manuali duplicati.
Il titolo e i controlli Fluent restano; gli angoli rispettano la decorazione
nativa, senza superficie per-pixel. Non viene forzato alcun attributo DWM.

Su Windows 11 il bridge restituisce HTMAXBUTTON sopra il pulsante Ingrandisci;
il sistema gestisce i messaggi mouse non-client. Il bridge è registrato dopo
WindowChrome. Il test HWND reale conferma HTMAXBUTTON=9 e assenza WS_EX_LAYERED.
Le animazioni native percepite e il flyout Snap non sono certificati da questi
due controlli. Il minimo 720 DIP continua a limitare alcuni layout Snap.

Fonti: [WindowChrome](https://learn.microsoft.com/en-us/dotnet/api/system.windows.shell.windowchrome),
[Snap Layouts](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-snap-layout-menu).

## Motion e stati

Feedback checkbox tramite opacity breve (160 ms); dettagli aperti in 160 ms e
chiusi in 120 ms. Nessuna animazione di dimensioni, posizione della finestra o
altezza delle righe. Hover riutilizza gli stili esistenti. La preferenza
SystemParameters.ClientAreaAnimation viene letta a ogni transizione.

La barra indeterminata esistente viene riutilizzata. La risorsa dinamica
ClientAreaAnimationKey controlla il pulse tramite Tag (riservato in questo
stile alla preferenza motion); la condizione è un booleano tipizzato. Test
reale: Tag=true attiva il clock, Tag=false lo rimuove, senza modificare Windows.
Le transizioni brevi già iniziate possono terminare entro 160 ms.

Durante l'installazione la barra della finestra di progresso è indeterminata;
non espone una percentuale non attestata del singolo installer. Il risultato
terminale usa le classi già esistenti, distinguendo errori, annullamenti e
verifica richiesta. Non sono stati introdotti stati fittizi o cambiamenti al
motore. Fasi e barra della scansione sono preservate.

## Note

Il pulsante Novità nella riga apre un unico pannello sulla pagina Aggiornamenti.
Mostra nome, versioni, origine, testo selezionabile/scorrevole e link validato.
Testo e nome accessibile del pulsante sono risorse IT/EN compatibili col riciclo.
Escape/Chiudi restituiscono focus solo se la riga rappresenta ancora lo stesso
elemento. La navigazione Tab resta nel pannello. Cambio pagina/unload annullano
la richiesta e collassano il pannello.

WinGet: richiesta on-demand al repository ufficiale, percorso ID/versione esatti;
manifest versione/singleton, locale IT/EN, poi DefaultLocale dichiarata. Ogni
documento deve confermare identità/versione e la locale deve corrispondere.
Solo ReleaseNotes e ReleaseNotesUrl: nessuna descrizione generica riciclata come
changelog. Parser limitato agli scalari canonici e blocchi YAML; forme non
supportate producono indisponibilità. Nessuno scraping della destinazione.

HTTP asincrono, timeout totale 12 secondi, limite 256 KiB per documento, niente
redirect automatici, niente cache persistente. Le risposte obsolete non possono
sostituire la richiesta corrente. Il link è HTTPS assoluto, senza credenziali,
IP o loopback. La provenienza dal manifest non è certificazione indipendente del
dominio. HTML/script sono solo testo. Nessun browser incorporato.

WUA: ReleaseNotes dello stesso oggetto, MoreInfoUrls o KB numerico; Description
resta etichettata separatamente. Letture opzionali e rilascio COM isolati dagli
errori della scansione. Driver di catalogo: link ufficiale già disponibile.
I metadati sono esclusi da piano, snapshot, consenso, identità e selezionabilità.

Fonti: [schema locale WinGet](https://github.com/microsoft/winget-pkgs/blob/master/doc/manifest/schema/1.28.0/locale.md),
[IUpdate.ReleaseNotes](https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nf-wuapi-iupdate-get_releasenotes),
[IUpdate](https://learn.microsoft.com/en-us/windows/win32/wua_sdk/iupdate-properties).

## Evidenze e limiti

Host osservato Windows 11 build 26320. Harness con root GUID isolata, settings
sintetici, azioni reali disabilitate. Finestra WPF reale aperta e chiusa durante
la verifica; immagini RenderTargetBitmap della finestra, nessuna cattura desktop
persistente. `_output/ux-20261109/notes-{it,en}-{Chiaro,Scuro}.png`.

Test mirati:

- `--update-experience`: versione esatta, mismatch/duplicati, blocchi multilinea,
  link non sicuri, fallback fr-FR, offline, annullamento, invarianti selezione,
  WUA note/descrizione/KB.
- `--update-experience-ui`: WPF IT/EN e temi, 720x500, testo lungo copiabile,
  scrolling, 500 righe con meno di 80 DataGridRow realizzate, risposta obsoleta,
  riapertura dopo fade (attesa dispatcher 180 ms), pulse e riduzione motion,
  HWND non layered e hit test maximize.
- `--updates-grid`, `--winget-hardening`, `--install-safety`, `--release-audit`.
- `--runner-consent`: 25 PASS, zero FAIL, effetti simulati.
- Build Release soluzione e harness; `git diff --check`.

Nessun benchmark generale: il controllo prestazionale è la conservazione della
virtualizzazione e l'assenza di richieste di rete per riga. Non sono stati
eseguiti upgrade reali né una nuova scansione WUA. Windows 10 reale, DPI nativi
150/200%, cambio monitor, taskbar, Snap al mouse, percezione delle animazioni e
Narrator restano verifiche manuali pendenti. Non è stata cambiata la scala PC.
M6-B resta WINDOWS VALIDATION PENDING; M8 finale separata.

Due analisti in sola lettura hanno verificato finestra e note. Corretti i rilievi
su ordine degli hook, mouse NC, KB, cancellazione, risorse localizzate e focus.
I test hanno riprodotto e corretto anche chiusura tardiva e trigger pulse tipizzato.

## Git e consolidamento

Nessun commit nuovo: il patch incrementale sui tre file condivisi non si applica
all'indice privo del lavoro A. Indice lasciato vuoto. Le modifiche nuove restano
in working tree insieme alle precedenti, senza push/merge/tag/release.
I file preesistenti non condivisi sono stati verificati identici byte per byte.
Snapshot di partenza e patch incrementale dei tre file condivisi si trovano in
`_output/ux-20261109/baseline/` e `ux-shared.patch`, esclusi da Git.

Per consolidare in seguito: conservare/chiudere prima il checkpoint A; confrontare
i tre file condivisi con gli snapshot, quindi applicare e revisionare il solo
incremento UX. Gli altri file UX sono App.xaml, Models/UpdateItem.cs,
Services/LocalizationService.cs, Services/WindowsUpdateService.cs,
UpdateProgressWindow.xaml/.cs, Controls/ReleaseNotesPanel.cs,
MainWindow.UpdatesExperience.cs, Services/NativeCaptionBridge.cs,
Services/ProviderReleaseNotes.cs, Services/ReleaseNotesService.cs,
Services/UiMotion.cs, i due UpdateExperienceTests/UpdateExperienceUiTests e questo
rapporto. Non includere i rapporti storici o gli altri file A incidentalmente.
