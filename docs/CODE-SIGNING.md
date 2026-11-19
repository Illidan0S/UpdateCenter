# Firma Authenticode delle release di Update Center

Le versioni 2.0.0 e 2.0.1 hanno eccezioni distinte in `UpdateCenter.csproj`. Il workflow richiede corrispondenza esatta fra versione, tag e policy, verifica gli hash e rifiuta di sostituire gli asset pubblicati di entrambe. Le versioni successive richiedono Authenticode.

Per le release stabili firmate servono tre valori configurati nell'ambiente GitHub Actions.

- Il segreto `WINDOWS_SIGNING_PFX_BASE64` contiene il PFX del publisher codificato in Base64.
- Il segreto `WINDOWS_SIGNING_PFX_PASSWORD` contiene la password del PFX.
- La variabile `WINDOWS_SIGNING_CERT_SHA256` contiene le 64 cifre esadecimali dello SHA-256 del certificato pubblico del firmatario in formato DER. Il responsabile della release deve confermare il certificato tramite un canale indipendente prima di configurare il valore. Per un certificato DER verificato, usare `Get-FileHash -LiteralPath publisher.cer -Algorithm SHA256`.

Conservare il PFX e la password nel gestore dei segreti. Quando il publisher rinnova il certificato, verificare e aggiornare anche la variabile SHA-256.

Per ogni tag stabile il workflow verifica il commit e la versione del progetto. Per una release firmata, firma il Portable con digest SHA-256 e timestamp RFC 3161, costruisce il Setup con quel Portable e firma il Setup. Verifica la firma incorporata, l'attendibilità Windows, il timestamp e il certificato atteso per entrambi gli EXE. Il job di release ripete le verifiche previste dalla policy e confronta i checksum prima del caricamento su GitHub.

Un build locale o di pull request senza tag può usare esplicitamente `-AllowUnsignedDevelopment` con tutte le variabili di firma assenti. La configurazione parziale provoca un errore. Per verificare un file già firmato, configurare solo la variabile del certificato e usare `./sign-windows-artifact.ps1 -Path <exe> -VerifyOnly`.

SmartScreen valuta anche la reputazione del programma e del publisher.

## Aggiornamento dall'app

La 2.0.0 corretta ammette un successore senza Authenticode solo se la release stabile ufficiale contiene il manifesto del tag autorizzato. Per una futura versione, il manutentore imposta `UnsignedReleaseVersion` uguale a `Version` e autorizza il tag letterale con `UNSIGNED_UPDATE_APPROVED_TAG`. Il workflow controlla il repository, la ref e il commit e crea il manifesto con gli SHA-256 dei due EXE definitivi. Il client verifica il digest GitHub del manifesto, il suo contenuto, il digest dell’asset, il file di checksum, l’origine dell’installazione e i byte scaricati. La 2.0.1 ritirata viene esclusa. La fiducia per le release senza firma dipende dal controllo del repository ufficiale.

Il Portable conserva una copia della versione precedente fino alla conferma del nuovo avvio. Per il Setup il recupero dopo un avvio applicativo fallito richiede la reinstallazione verificata del pacchetto precedente nello stesso profilo.

### Comportamento delle release precedentemente distribuite

Il client controlla il checksum del download e usa il provider Authenticode di Windows per verificare le firme incorporate dell'app in esecuzione e del nuovo Portable o Setup. Entrambe devono essere attendibili, con timestamp e con lo stesso certificato del publisher. Nel percorso Portable la verifica viene ripetuta prima della sostituzione.

Per il solo passaggio Portable ufficiale 1.1.5 → 2.0.0, il candidato riconosce l'EXE precedente tramite SHA-256 `00912E56944F84B243ECF0F126CA2E67055F9BD3D1EAC98567E42636A198F826`. Il digest coincide con quello dell'asset GitHub 550111885 e con il relativo sidecar. Prima di sostituire l'EXE, la 2.0.0 senza firma richiede tag stabile e asset esatto del repository ufficiale, digest API, checksum con nome esatto e hash dei byte concordanti. Il Setup 2.0.0 scaricato in modalità `/SELFUPDATE` verifica nuovamente il sidecar ufficiale e il proprio hash prima dell'installazione. Entrambi limitano le richieste a HTTPS e i redirect ai CDN GitHub previsti. [Asset e checksum della 1.1.5](https://github.com/Illidan0S/UpdateCenter/releases/tag/v1.1.5)

Il client 1.1.5 avvia il candidato con le verifiche già presenti nella 1.1.5. Le nuove verifiche proteggono il confine di sostituzione e installazione. Il modello di fiducia della 2.0.0 senza firma dipende dal repository GitHub e da HTTPS. I checksum verificano l’integrità dei file. La firma Authenticode resta il controllo del publisher riconosciuto da Windows.

La **2.0.0 già distribuita** richiede una firma valida con timestamp su ogni versione successiva, prima di avviare il Setup o il Portable. Il passaggio alla 2.0.1 senza firma avviene **manualmente**. Chiudi Update Center, scarica l’asset esatto e il relativo `.sha256` dalla release ufficiale. Verifica che `Get-FileHash -Algorithm SHA256 -LiteralPath <file>` coincida con il checksum pubblicato. Avvia il Setup con lo stesso account Windows o sostituisci il Portable nella sua cartella. [Controllo nel client 2.0.0 pubblicato](https://github.com/Illidan0S/UpdateCenter/blob/a08a0d572fb61ea607850cfa203362032f772c8b/Services/ReleasePublisherVerifier.cs#L63)

Il Portable ufficiale 1.1.5 può passare direttamente alla 2.0.1. La 1.1.5 controlla il checksum prima di avviare il candidato. Prima della sostituzione, la 2.0.1 richiede il vecchio digest noto e verifica il tag stabile, il nome e l’URL esatti, il digest API GitHub, il checksum e i byte del nuovo Portable. Il Setup senza firma 2.0.1 richiede le stesse prove di pubblicazione per il percorso `/SELFUPDATE` dei client 1.1.4 e 1.1.5, oltre all’identità dell’utente e alla directory registrata. I client precedenti che avviano un Setup silenzioso senza `/SELFUPDATE` usano il percorso manuale interattivo.

Il client 2.0.1 senza firma richiede firma Windows attendibile e timestamp su qualsiasi successore. Verifica i byte della copia in uso e del pacchetto candidato attraverso i relativi asset ufficiali. Un successore Portable con firma può verificare di nuovo la provenienza della 2.0.1 e richiede il pin del proprio publisher prima della sostituzione. La fiducia nella 2.0.1 senza firma dipende dal repository ufficiale e da HTTPS. I checksum verificano l’integrità dei file. Windows o SmartScreen possono mostrare un avviso.

Per le versioni con un updater incompatibile, il passaggio è diretto tramite il Setup avviato nello stesso profilo o la sostituzione manuale del Portable a processo chiuso. Il Setup conserva l’AppId della linea Setup precedente e riusa la directory già registrata. Impostazioni e cronologia sono nel profilo Windows, fuori dalla directory dell’eseguibile.

Le attestazioni di GitHub Actions sono verificabili separatamente con `gh attestation verify` e legano i file al workflow di build. La 1.1.5 non le consulta e la policy di aggiornamento non ne dipende. [Attestazioni GitHub Actions](https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations)

Il Portable 1.1.5 passa alla nuova versione il percorso dell'EXE originale. I nomi arbitrari vengono rifiutati dal vecchio client prima del passaggio. Il backup viene rimosso dopo che la nuova finestra ha confermato il primo rendering tramite il canale del processo di aggiornamento. Un avvio fallito lascia la versione precedente ripristinata quando il processo figlio può essere arrestato e il file può essere sostituito in sicurezza. Negli altri casi il backup resta disponibile.

Nel Setup, `/SELFUPDATE` richiede che il pacchetto provenga dalla cartella Updates del profilo del token corrente e che proprietario, registrazione per utente e destinazione concordino. Il Setup rifiuta una richiesta con credenziali di un altro account quando la continuità del proprietario non è dimostrata. La 1.1.5 usa `runas` prima di avviare il candidato. Una policy che impedisce l'elevazione blocca il passaggio già nel client legacy.

Il self-update richiede un client Windows x64 ammesso dalla matrice del candidato. I controlli precedono la modifica dell'installazione. Se il runtime del Portable non si avvia, la 1.1.5 può già essersi chiusa dopo aver avviato il processo, ma il suo EXE resta utilizzabile.

Per le altre build senza firma e per il rinnovo con un certificato diverso, usare la [pagina release ufficiale](https://github.com/Illidan0S/UpdateCenter/releases) e verificare il firmatario tramite un canale indipendente.

## MSIX

Update Center usa processi elevati, installazione driver e self-update. Una migrazione a MSIX richiede la verifica di queste funzioni con il relativo modello di sicurezza e distribuzione.
