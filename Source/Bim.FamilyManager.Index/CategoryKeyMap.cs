namespace Bim.FamilyManager.Index;

/// <summary>
///     Maps localized Revit family category names to a language-independent category key.
/// </summary>
/// <remarks>
///     Family files store their category localized to the language the family was authored in
///     ("Furniture" vs "Mobiliario") and carry no stable identifier (see ARCHITECTURE.md). This map
///     unifies the known localizations of the standard loadable-family categories under one stable
///     key, so filters can treat "Furniture" and "Mobiliario" as the same category.
///     The table deliberately contains only translations that have been verified; categories outside
///     the map simply keep a <c>null</c> key and remain filterable by their localized text. Entries
///     are extended as further languages or categories are confirmed.
/// </remarks>
public static class CategoryKeyMap
{
    private static readonly Dictionary<string, string> TermToKey = BuildMap(new (string Key, string[] Terms)[]
    {
        ("casework", ["Casework", "Muebles de obra"]),
        ("columns", ["Columns", "Columnas"]),
        ("doors", ["Doors", "Puertas"]),
        ("entourage", ["Entourage", "Entorno"]),
        ("furniture", ["Furniture", "Mobiliario"]),
        ("furniture-systems", ["Furniture Systems", "Sistemas de mobiliario"]),
        ("generic-models", ["Generic Models", "Modelos genéricos"]),
        ("level-heads", ["Level Heads", "Extremos iniciales de nivel"]),
        ("lighting-fixtures", ["Lighting Fixtures", "Luminarias"]),
        ("mass", ["Mass", "Masa"]),
        ("mechanical-equipment", ["Mechanical Equipment", "Equipos mecánicos"]),
        ("parking", ["Parking", "Aparcamiento"]),
        ("planting", ["Planting", "Vegetación"]),
        ("plumbing-fixtures", ["Plumbing Fixtures", "Aparatos sanitarios"]),
        ("profiles", ["Profiles", "Perfiles"]),
        ("site", ["Site", "Emplazamiento"]),
        ("specialty-equipment", ["Specialty Equipment", "Equipos especializados"]),
        ("structural-columns", ["Structural Columns", "Pilares estructurales"]),
        ("structural-foundations", ["Structural Foundations", "Cimentación estructural"]),
        ("structural-framing", ["Structural Framing", "Armazón estructural"]),
        ("structural-rebar-couplers", ["Structural Rebar Couplers", "Acopladores de armadura estructural"]),
        ("windows", ["Windows", "Ventanas"])
    });

    /// <summary>
    ///     Attempts to resolve the language-independent key for a localized category term.
    /// </summary>
    /// <param name="localizedTerm">The category term as stored in the family file.</param>
    /// <param name="key">The stable key, or <c>null</c> when the method returns <c>false</c>.</param>
    /// <returns><c>true</c> if the term is a known localization of a mapped category; otherwise <c>false</c>.</returns>
    public static bool TryGetKey(string? localizedTerm, out string? key)
    {
        key = null;
        if (string.IsNullOrWhiteSpace(localizedTerm))
        {
            return false;
        }

        return TermToKey.TryGetValue(localizedTerm.Trim(), out key!) && key is not null;
    }

    private static Dictionary<string, string> BuildMap((string Key, string[] Terms)[] entries)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, terms) in entries)
        {
            foreach (var term in terms)
            {
                map[term] = key;
            }
        }

        return map;
    }
}
