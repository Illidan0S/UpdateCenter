# Consolidamento UI, consenso e affidabilità R1/R2

Checkpoint locale integrato del 10 novembre 2026, successivo a `33f5468d`.
Il consolidamento non modifica il comportamento applicativo rispetto alla
baseline verificata R2. Le note precedenti che riportano commit sospesi o working
tree non consolidato descrivono lo stato storico dei rispettivi interventi.

## Composizione e dipendenze

| Area | Contenuto |
| --- | --- |
| UI Fluent | Stato e Cronologia con dettagli completi, scorrimento driver e dialoghi, selezione/focus/virtualizzazione, impostazioni e note del provider |
| Consenso M6-B | Riepilogo unico, preparazione del piano, canale autenticato e autorizzazione monouso vincolata allo snapshot |
| Runner/7-Zip | Preflight asincrono indipendente dal dispatcher, deadline read-only, cancellazione, fasi reali e warning di stallo |
| WinGet B2/R1 | Identità/scope/account verificati, requisiti dei manifest, diagnosi, esiti e verifica separata dal successo della query |
| R2 | UAC nativo per installer macchina tradizionali univoci e vincolati, requisito congelato, gestione del rifiuto, riepilogo dei privilegi |
| Test/documentazione | Harness console isolati, regressioni di consenso/preflight/esiti e prove WPF |

Queste aree condividono MainWindow, LocalizationService, runner, modelli e
dispatcher dei test. Un checkpoint integrato evita stati intermedi incompleti
senza ricostruire artificialmente gli snapshot storici. Nessun output generato,
log runtime, inventario locale o configurazione personale è incluso.

## R1 e R2: comportamento e limiti

La query di inventario riuscita non dimostra la versione installata. Gli esiti
conservano fallimento, annullamento e verifica pendente; la suppression di una
coppia di versioni non riclassifica un tentativo già fallito. La diagnostica
tecnica resta disponibile separatamente dal messaggio utente IT/EN.

R2 recupera il requisito `elevationRequired` soltanto per una ricetta macchina
EXE/Inno/Nullsoft/Burn univoca: identità ARP/ProductCode, architettura, origine,
versione, manifest e client verificati, senza dipendenze dichiarate o servizi
pacchettizzati. WinGet richiede UAC quando necessario; UI e runner software
restano nel contesto originale. Nessun broker amministrativo generico o retry
privilegiato automatico. Dopo interruzione l'installer elevato potrebbe essere
ancora attivo: non viene promessa la sua terminazione.

MSIX/AppX e MSI/WiX non sono recuperati automaticamente da questa estensione.
Occorrono prove equivalenti di selezione, identità, contesto e verifica finale.
L'esecuzione resta vincolata ai controlli del client WinGet: il binding del
manifest non equivale a un trasferimento diretto di un payload congelato.

## Evidenze di verifica della baseline

La baseline R2 ha superato build Release senza errori/avvisi, 15 gruppi motore R2,
14 gruppi R1, 12 scenari R2 dei dialoghi/privilegi, 16 scenari di scroll R1,
regressioni M5/B2, 25 casi M6-B, attese runner, esiti Audacity e consenso unico
con processi separati simulati. I test UI usano fixture e root dati GUID isolate.
I risultati simulati non attestano installazioni reali o un tasso di successo.
Le verifiche native di compatibilità Windows rimangono limitate alle evidenze
precedentemente documentate; target minimo Windows 10 build 17763 invariato.

Comandi mirati riproducibili dopo build Release:

```powershell
dotnet build UpdateCenter.sln -c Release --no-restore
# Eseguire l'harness console compilato con gli argomenti pertinenti:
# --r2-engine, --r1-engine, --r2-dialogs, --r1-dialog-scroll
# --install-safety, --runner-consent, --runner-waits
# --installation-outcomes, --unified-consent, --unified-consent-ui
```

Non usare `dotnet test` come prova dell'esecuzione degli harness console.
I log, le immagini e il backup verificato restano artefatti locali esclusi da Git.

## Prima correzione applicativa successiva: versioni software

R3 ha riprodotto che `WinGetService.IsVersionAtLeast` riusa il comparatore dei
driver e può considerare uguali versioni software diverse non interpretabili
numericamente. Il comportamento NON è corretto da questo consolidamento.

L'intervento successivo deve introdurre un esito esplicito non confrontabile,
separare la disponibilità della query dalla prova del target e preservare i
formati numerici equivalenti. Per versioni opache, uguaglianza testuale esatta
oppure regole autorevoli del provider; mai promuovere l'incertezza a successo.
Il comparatore dei driver non deve essere modificato globalmente. Le fixture
copriranno versioni non numeriche, prerelease/build, risposte ambigue, origine
errata, verifica pendente, annullamento e coerenza di riepilogo/Stato/Cronologia.
Nessun recupero dei 14 blocchi dichiarati è rivendicato senza inventario attuale.
