using UpdateCenter.Models;

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

Console.WriteLine("Smoke test superati: parsing, ordinamento semantico e impostazioni updater.");
