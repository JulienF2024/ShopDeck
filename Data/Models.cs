namespace ShopDeck.Data;

public class Machine
{
    public string UnitId { get; set; } = "";        // 71HTR116
    public string? LegacyId { get; set; }            // 040-116
    public string Family { get; set; } = "";         // 793F
    public string Model { get; set; } = "";          // CAT 793F
    public string? Year { get; set; }
    public string? Vin { get; set; }
    public string? Engine { get; set; }
    public string? EngineVin { get; set; }
    public string? PhotoPath { get; set; }           // relatif a la cle, assigne par Julien
    public string? Notes { get; set; }
}

public class DocItem
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string RelPath { get; set; } = "";        // relatif a G:\files
    public string Ext { get; set; } = "";
    public long SizeBytes { get; set; }
    public string? System { get; set; }              // Schematics, MEMS, DSS, Minestar...
    public string? Folder { get; set; }              // dossier virtuel effectif sur la fiche machine
    public bool IsDuplicate { get; set; }
}

public class LauncherEntry
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Target { get; set; } = "";         // chemin exe/bat, relatif cle ou absolu
    public string? Args { get; set; }
    public string? WorkingDir { get; set; }
    public string Kind { get; set; } = "gui";        // gui | console | web
    public string? IconPath { get; set; }
    public bool TryEmbed { get; set; }               // embedding SetParent experimental
}
