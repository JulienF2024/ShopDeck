using System.IO;
using System.Text.Json;
using ShopDeck.Data;

namespace ShopDeck.Core;

// Lit equipment_fleet.json (125 unites legacy) -> table machines en format 71xxx.
public class FleetImporter
{
    private readonly Db _db;
    public FleetImporter(Db db) => _db = db;

    private class Row
    {
        public JsonElement unit { get; set; }
        public JsonElement manufacturer { get; set; }
        public JsonElement model { get; set; }
        public JsonElement year { get; set; }
        public JsonElement vin { get; set; }
        public JsonElement engine { get; set; }
        public JsonElement engine_vin { get; set; }
    }

    public int Import(string jsonPath)
    {
        if (!File.Exists(jsonPath)) return 0;
        var rows = JsonSerializer.Deserialize<Row[]>(File.ReadAllText(jsonPath)) ?? System.Array.Empty<Row>();

        var deleted = _db.GetDeletedMachines();
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        int n = 0;

        foreach (var r in rows)
        {
            var legacy = JsonToStr(r.unit) ?? "";
            if (string.IsNullOrWhiteSpace(legacy)) continue;
            if (UnitCodec.IsExcluded(legacy)) continue; // 011/012 retires
            var unitId = UnitCodec.LegacyToNew(legacy) ?? legacy; // fallback: garde le legacy si pas de code
            if (deleted.Contains(unitId)) continue;   // retiree a la main par Julien, on respecte
            var mfr = JsonToStr(r.manufacturer);
            var mdl = JsonToStr(r.model);
            var model = string.IsNullOrEmpty(mfr) ? (mdl ?? "") : $"{mfr} {mdl}";

            var m = new Machine
            {
                UnitId = unitId,
                LegacyId = legacy,
                Family = NormalizeFamily(mdl),
                Model = model,
                Year = JsonToStr(r.year),
                Vin = JsonToStr(r.vin),
                Engine = JsonToStr(r.engine),
                EngineVin = JsonToStr(r.engine_vin)
            };
            _db.UpsertMachine(c, m);
            n++;
        }
        tx.Commit();
        return n;
    }

    // regroupe les variantes sous une famille propre pour l'affichage/photo par defaut
    public static string NormalizeFamily(string? model)
    {
        if (string.IsNullOrEmpty(model)) return "Autre";
        var m = model.ToUpperInvariant();
        if (m.StartsWith("793")) return "793F";
        if (m.StartsWith("PV")) return "PV-235";
        if (m.StartsWith("777") || m.StartsWith("725")) return "VSE";
        if (m.StartsWith("740") || m.StartsWith("745")) return "ATR";
        if (m.StartsWith("D10")) return "D10T";
        if (m.StartsWith("16M") || m.StartsWith("14M")) return "Grader";
        if (m.StartsWith("374")) return "374";
        if (m.StartsWith("854") || m.StartsWith("834")) return "Wheeldozer";
        return model;
    }

    private static string? JsonToStr(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number => e.GetRawText(),
        JsonValueKind.String => e.GetString(),
        _ => null
    };
}
