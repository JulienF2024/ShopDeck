using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ShopDeck.Core;

// Decode legacy <PREFIX>-<NUM> <-> nouveau format 71<CODE><NUM>
// Table de C:\ClaudeHub\cat-manuals\equipment_fleet_index.md
public static class UnitCodec
{
    // prefix -> (code nouveau, offset applique au numero)
    // offset: 042 = +300, 054 = +100, les autres = 0
    private static readonly Dictionary<string, (string code, int offset)> Map = new()
    {
        ["010"] = ("DRL", 0),   // Epiroc PV-235 (drills)
        ["020"] = ("EXC", 0),   // excavatrices
        ["021"] = ("EXC", 0),   // excavatrices
        ["022"] = ("EXC", 0),   // excavatrices
        ["030"] = ("LOA", 0),   // loaders
        ["031"] = ("LOA", 0),   // loaders
        ["040"] = ("HTR", 0),
        ["041"] = ("ATR", 0),
        ["042"] = ("VSE", 300),
        ["050"] = ("DOZ", 0),
        ["052"] = ("GRA", 0),
        ["053"] = ("GRA", 0),
        ["054"] = ("DOZ", 100),
    };

    // prefixes exclus completement de l'app (Julien: retirer 011 et 012)
    private static readonly HashSet<string> Excluded = new()
    {
        "011","012"
    };

    // prefixes connus sans code nouveau documente: on garde l'ID legacy comme cle
    private static readonly HashSet<string> NoNewCode = new()
    {
        "013","051","057","065"
    };

    // vrai si l'ID legacy appartient a un prefix banni (a filtrer a l'import et au scan)
    public static bool IsExcluded(string legacy)
    {
        var m = LegacyRx.Match(legacy);
        return m.Success && Excluded.Contains(m.Groups["p"].Value);
    }

    private static readonly Regex LegacyRx = new(@"(?<p>0\d{2})[-\s]?(?<n>\d{3})", RegexOptions.Compiled);
    private static readonly Regex NewRx = new(@"71(?<c>[A-Z]{3})(?<n>\d{3})", RegexOptions.Compiled);

    // "Truck 040-116 WS" -> "71HTR116" ; retourne null si aucun ID reconnu
    public static string? LegacyToNew(string legacy)
    {
        var m = LegacyRx.Match(legacy);
        if (!m.Success) return null;
        var p = m.Groups["p"].Value;
        if (Excluded.Contains(p)) return null; // 011/012 bannis
        var n = int.Parse(m.Groups["n"].Value);
        if (Map.TryGetValue(p, out var v))
            return $"71{v.code}{(n + v.offset):D3}";
        if (NoNewCode.Contains(p))
            return $"{p}-{n:D3}"; // pas de code: on conserve le legacy comme identifiant
        return null;
    }

    // extrait tous les unit-ids (nouveaux) mentionnes dans un chemin/nom de fichier
    public static HashSet<string> ExtractUnits(string text)
    {
        var found = new HashSet<string>();
        foreach (Match m in NewRx.Matches(text))
            found.Add(m.Value.ToUpperInvariant());
        foreach (Match m in LegacyRx.Matches(text))
        {
            var conv = LegacyToNew(m.Value);
            if (conv != null) found.Add(conv);
        }
        return found;
    }

    // legacy canonique a partir d'un nouveau format, pour affichage (best effort)
    public static string? NewToLegacy(string unitId)
    {
        var m = NewRx.Match(unitId);
        if (!m.Success) return null;
        var code = m.Groups["c"].Value;
        var n = int.Parse(m.Groups["n"].Value);
        foreach (var kv in Map)
            if (kv.Value.code == code)
                return $"{kv.Key}-{(n - kv.Value.offset):D3}";
        return null;
    }
}
