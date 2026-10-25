using System.Text.Json;
using System.Text.Json.Serialization;

namespace NFOForge.Compliance;

public static class RuleSetRepository
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true
    };
    public static RuleSet Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var result = JsonSerializer.Deserialize<RuleSet>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("Référentiel vide.");
        if (string.IsNullOrWhiteSpace(result.Version) || string.IsNullOrWhiteSpace(result.Profile)
            || string.IsNullOrWhiteSpace(result.Category) || result.Rules is null)
            throw new InvalidDataException("Métadonnées du référentiel invalides.");
        if (result.Rules.Any(r => string.IsNullOrWhiteSpace(r.Id) || string.IsNullOrWhiteSpace(r.Condition))
            || result.Rules.Select(r => r.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Rules.Count)
            throw new InvalidDataException("Identifiants ou conditions de règles invalides.");
        return result;
    }
    public static void Save(string path, RuleSet rules) =>
        File.WriteAllText(path, JsonSerializer.Serialize(rules, Options));
}
