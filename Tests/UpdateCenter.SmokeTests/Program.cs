using UpdateCenter.Models;
using UpdateCenter.Services;
using System.Reflection;
using System.Text.Json;

var parsingCases = new Dictionary<string, SemanticVersion>
{
    ["1.0.0"] = new(1, 0, 0),
    ["v1.0.1"] = new(1, 0, 1),
    ["1.1.0"] = new(1, 1, 0)
};

foreach (var (text, expected) in parsingCases)
{
    if (!SemanticVersion.TryParse(text, out var parsed) || parsed != expected)
        throw new InvalidOperationException($"Parsing semantico non riuscito per {text}.");
}

foreach (var invalid in new[] { "", "1.0", "1.0.0.0", "v1.1.0-beta", "release-1.0.0" })
{
    if (SemanticVersion.TryParse(invalid, out _))
        throw new InvalidOperationException($"Versione non valida accettata: {invalid}.");
}

var v100 = new SemanticVersion(1, 0, 0);
var v101 = new SemanticVersion(1, 0, 1);
var v110 = new SemanticVersion(1, 1, 0);
if (!(v100 < v101 && v101 < v110 && v110 > v100))
    throw new InvalidOperationException("Ordinamento semantico non valido.");
if (typeof(AppSettings).Assembly.GetName().Version?.ToString(3) != "1.0.5")
    throw new InvalidOperationException("La versione dell'assembly non corrisponde alla build 1.0.5.");

var settings = new AppSettings();
if (!settings.CheckAppUpdatesAutomatically)
    throw new InvalidOperationException("Il controllo automatico deve essere attivo per impostazione predefinita.");
if (!settings.NotifyWhenUpdatesAreAvailable || settings.LanguageMode != "it" || settings.AutomaticScanInterval != "Off")
    throw new InvalidOperationException("Le nuove preferenze predefinite non sono valide.");

var smallScale = TypographyOptions.ScaleFor("Piccola");
var mediumScale = TypographyOptions.ScaleFor("Media");
var largeScale = TypographyOptions.ScaleFor("Grande");
if (smallScale != 1.10 || !(smallScale < mediumScale && mediumScale < largeScale))
    throw new InvalidOperationException("La progressione delle dimensioni del testo non è valida.");

var legacyMediumSettings = new AppSettings { DefaultsRevision = 1, FontSizeMode = "Media" };
if (!legacyMediumSettings.ApplyMigrations() || legacyMediumSettings.FontSizeMode != "Piccola")
    throw new InvalidOperationException("La vecchia dimensione Media non è stata migrata a Piccola.");
var legacyLargeSettings = new AppSettings { DefaultsRevision = 1, FontSizeMode = "Grande" };
legacyLargeSettings.ApplyMigrations();
if (legacyLargeSettings.FontSizeMode != "Grande")
    throw new InvalidOperationException("La preferenza Grande non deve essere ridotta durante la migrazione.");

if (DriverVersionComparer.Compare("32.0.21043.19003", "32.0.21043.1000") <= 0 ||
    DriverVersionComparer.Compare("6.0.9954.1", "6.0.9954.1") != 0 ||
    DriverVersionComparer.Compare("25.040.2.218", "25.40.2.217") <= 0)
    throw new InvalidOperationException("Confronto delle versioni driver non valido.");

var wingetItalian = string.Join('\n',
    $"{"Nome",-24}{"Id",-25}{"Versione",-16}{"Disponibile",-16}Origine",
    new string('-', 90),
    $"{"Opera GX Stable",-24}{"Opera.OperaGX",-25}{"133.0.5932.39",-16}{"133.0.5932.56",-16}winget");
var wingetEnglish = string.Join('\n',
    $"{"Name",-24}{"Id",-25}{"Version",-16}{"Available",-16}Source",
    new string('-', 90),
    $"{"PowerToys (Preview)",-24}{"Microsoft.PowerToys",-25}{"0.90.0",-16}{"0.91.0",-16}winget");
var parsedItalian = WinGetService.ParseUpgradeTable(wingetItalian);
var parsedEnglish = WinGetService.ParseUpgradeTable(wingetEnglish);
if (parsedItalian.Count != 1 || parsedItalian[0].Id != "Opera.OperaGX" ||
    parsedItalian[0].AvailableVersion != "133.0.5932.56")
    throw new InvalidOperationException("Parsing della tabella WinGet italiana non riuscito.");
if (parsedEnglish.Count != 1 || parsedEnglish[0].Id != "Microsoft.PowerToys")
    throw new InvalidOperationException("Parsing della tabella WinGet inglese non riuscito.");

if (WinGetService.ClassifyOutcome(new ProcessResult(unchecked((int)0x8A15002B), "", "")) != UpdateOutcomes.NotApplicable ||
    WinGetService.ClassifyOutcome(new ProcessResult(unchecked((int)0x8A15008E), "", "")) != UpdateOutcomes.ManualRequired ||
    WinGetService.ClassifyOutcome(new ProcessResult(unchecked((int)0x8A150114), "", "")) != UpdateOutcomes.ManualRequired ||
    WinGetService.ClassifyOutcome(new ProcessResult(0, "", "")) != UpdateOutcomes.Completed)
    throw new InvalidOperationException("Classificazione degli esiti WinGet non valida.");

var safeManifest = "PackageIdentifier: Example.Safe\nInstallers:\n- Architecture: x64\n  UpgradeBehavior: install";
var destructiveManifest = "PackageIdentifier: Example.Risky\nInstallers:\n- Architecture: x64\n  UpgradeBehavior: uninstallPrevious";
var unknownManifest = "PackageIdentifier: Example.Unknown\nInstallerType: exe";
if (WinGetManifestSafetyService.ParseUpgradeSafety(safeManifest) != WinGetUpgradeSafety.Safe ||
    WinGetManifestSafetyService.ParseUpgradeSafety(destructiveManifest) != WinGetUpgradeSafety.RemovesPreviousVersion ||
    WinGetManifestSafetyService.ParseUpgradeSafety(unknownManifest) != WinGetUpgradeSafety.Unknown)
    throw new InvalidOperationException("Classificazione di sicurezza dei manifest WinGet non valida.");
var manifestUris = WinGetManifestSafetyService.BuildManifestUris("JetBrains.CLion", "2026.2");
if (!manifestUris[0].AbsoluteUri.EndsWith(
        "/manifests/j/JetBrains/CLion/2026.2/JetBrains.CLion.installer.yaml",
        StringComparison.Ordinal))
    throw new InvalidOperationException("Percorso del manifest WinGet non valido.");

var riskySelection = new UpdateItem
{
    Id = "Example.Risky",
    Name = "Risky package",
    Kind = UpdateKind.Software,
    RequiresRiskConfirmation = true
};
riskySelection.IsSelected = false;
if (!riskySelection.CanInstall || riskySelection.IsSelected || riskySelection.PriorityLabel != "Conferma")
    throw new InvalidOperationException("Gli aggiornamenti rischiosi devono restare installabili ma non preselezionati.");

var duplicateOperaRows = string.Join('\n',
    $"{"Nome",-36}{"Id",-20}{"Versione",-16}{"Disponibile",-16}Origine",
    new string('-', 96),
    $"{"Opera GX Stable 133.0.5932.39",-36}{"Opera.OperaGX",-20}{"133.0.5932.39",-16}{"133.0.5932.56",-16}winget",
    $"{"Opera GX Stable 133.0.5932.39",-36}{"Opera.OperaGX",-20}{"133.0.5932.39",-16}{"",-16}winget");
var parseRowsMethod = typeof(WinGetService).GetMethod("ParsePackageRows", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Parser interno WinGet non trovato.");
var duplicateParsedRows = parseRowsMethod.Invoke(null, [duplicateOperaRows])
    ?? throw new InvalidOperationException("Parsing delle righe duplicate WinGet non riuscito.");
var resolveMatchMethod = typeof(WinGetService).GetMethod("ResolveExactInstalledMatch", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Risoluzione della corrispondenza WinGet non trovata.");
var resolvedOpera = resolveMatchMethod.Invoke(null,
    [duplicateParsedRows, "Opera GX Stable 133.0.5932.39", "Opera.OperaGX"]);
if (resolvedOpera is null)
    throw new InvalidOperationException("Le righe WinGet duplicate dello stesso pacchetto non sono state unificate.");

LocalizationService.Initialize("en");
if (LocalizationService.Translate("Aggiornamenti") != "Updates")
    throw new InvalidOperationException("Traduzione inglese non disponibile.");
LocalizationService.Initialize("it");

var catalogAssembly = typeof(AppSettings).Assembly;
var catalogResource = catalogAssembly.GetManifestResourceNames()
    .SingleOrDefault(x => x.EndsWith("driver-catalog.json", StringComparison.OrdinalIgnoreCase))
    ?? throw new InvalidOperationException("Catalogo driver incorporato nei test non trovato.");
using (var catalogStream = catalogAssembly.GetManifestResourceStream(catalogResource)!)
using (var catalogJson = JsonDocument.Parse(catalogStream))
{
    if (catalogJson.RootElement.GetProperty("schemaVersion").GetInt32() != 1 ||
        catalogJson.RootElement.GetProperty("entries").ValueKind != JsonValueKind.Array)
        throw new InvalidOperationException("Schema del catalogo driver non valido.");
}

Console.WriteLine("Smoke test superati: versioni, WinGet, lingua, tipografia, migrazione e catalogo driver stabile.");
