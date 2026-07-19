using UpdateCenter.Models;
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

var settings = new AppSettings();
if (!settings.CheckAppUpdatesAutomatically)
    throw new InvalidOperationException("Il controllo automatico deve essere attivo per impostazione predefinita.");

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

var catalogResource = Assembly.GetExecutingAssembly().GetManifestResourceNames()
    .SingleOrDefault(x => x.EndsWith("driver-catalog.json", StringComparison.OrdinalIgnoreCase))
    ?? throw new InvalidOperationException("Catalogo driver incorporato nei test non trovato.");
using (var catalogStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(catalogResource)!)
using (var catalogJson = JsonDocument.Parse(catalogStream))
{
    if (catalogJson.RootElement.GetProperty("schemaVersion").GetInt32() != 1 ||
        catalogJson.RootElement.GetProperty("entries").ValueKind != JsonValueKind.Array)
        throw new InvalidOperationException("Schema del catalogo driver non valido.");
}

Console.WriteLine("Smoke test superati: versioni, updater, tipografia, migrazione e catalogo driver.");
