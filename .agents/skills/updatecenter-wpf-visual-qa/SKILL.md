---
name: updatecenter-wpf-visual-qa
description: Rivede l'interfaccia WPF di UpdateCenter con screenshot reali, accessibilità, DPI, temi e IT/EN. Usare per audit XAML e modifiche visive; non per redesign durante il retarget del framework.
---

# QA visiva WPF di UpdateCenter

Individuare la schermata e il suo stato; conservare azioni, consenso e semantica
degli esiti. La migrazione .NET non autorizza a riscrivere l'interfaccia.

## Prima di cambiare stili

- Esaminare `App.xaml`, `MainWindow.xaml`, `ThemeService`, `TypographyService`,
  `LocalizationService` e i dialoghi collegati. Controllare template e code-behind.
- Valutare prima `ThemeMode` e `PresentationFramework.Fluent` nativi WPF .NET 10.
  Provare un dialogo isolato: gli stili impliciti esistenti possono mascherare il
  tema. Non mescolare indiscriminatamente dizionari Fluent e template custom.
- Verificare breaking change XAML: definizioni Grid vuote, DynamicResource usati
  su proprietà non compatibili, ambiguità WPF/WinForms `MenuItem`/`ContextMenu`.

## Cosa verificare

- Gerarchia leggibile tra titolo, riepilogo, azione primaria e dettagli. Tipografia
  tramite risorse esistenti, spaziatura coerente e icone con testo/nome accessibile.
- Chiaro/scuro/sistema e contrasto elevato: testo normale 4.5:1, grande 3:1,
  focus e confini significativi 3:1. Non comunicare successo/rischio con solo colore.
- Tab/Shift+Tab, Invio/Spazio/Escape, focus visibile, selezione diversa da focus,
  `AutomationProperties.Name`, etichette associate e ordine della lettura.
- 1366×768 e 1920×1080, finestra minima e massimizzata, DPI 100/150/200%, cambio
  monitor se disponibile, dimensione testo grande. Cercare clipping e azioni fuori vista.
- IT e EN anche in DataTemplate, popup, menu contestuali, colonne e messaggi:
  la sola visita dell'albero logico non dimostra la traduzione dei template.
- Dashboard senza scansione; aggiornamenti vuoti/selezionati; driver senza catalogo
  e diagnostica; hardware in caricamento; runtime; cronologia vuota/dettagli;
  impostazioni; LAN disabilitata, offline, in attesa e collegata.
- Scansione, errore, annullamento, aggiornamento in corso/pausa/riavvio richiesto:
  stato e azioni abilitate devono corrispondere, senza avviare installazioni reali.

## Prove e consegna

Usare un profilo Windows/VM usa e getta per l'app: l'avvio e i test possono migrare
impostazioni e pulire AppPaths. Con un desktop disponibile acquisire screenshot
reali prima/dopo alle stesse dimensioni, DPI, lingua, tema e dati di fixture.
Annotare versione, schermata, stato e riproduzione; oscurare identificatori locali
prima di condividere. Non presentare un mockup come screenshot dell'applicazione.

Se desktop o isolamento mancano, dichiararlo e consegnare analisi statica e matrice
di verifica. Ogni rilievo deve avere evidenza, impatto e rimedio; una preferenza
decorativa è una proposta, non un difetto. Non cambiare DPI/tema globali del PC.
