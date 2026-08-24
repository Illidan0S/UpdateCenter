# Code signing e SmartScreen — Update Center 1.1.4

Le Release stabili richiedono firma Authenticode. Configurare i segreti GitHub Actions:

- `WINDOWS_SIGNING_PFX_BASE64`
- `WINDOWS_SIGNING_PFX_PASSWORD`

Il PFX e la password non devono mai essere salvati nel repository.

La pipeline firma prima il portable, costruisce l'installer con l'EXE firmato, firma l'installer,
verifica entrambe le firme e solo dopo calcola gli SHA-256 pubblicati.

SmartScreen non viene aggirato o disattivato: certificato attendibile e reputazione restano requisiti Windows.

## MSIX

MSIX non viene introdotto automaticamente nella 1.1.4: Update Center usa processi elevati,
installazione driver e self-update. Prima di migrare va validata la compatibilità con il modello
di sicurezza e deployment MSIX senza perdere funzioni o introdurre workaround insicuri.
