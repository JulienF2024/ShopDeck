using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;

namespace ShopDeck.Data;

public class Db
{
    private readonly string _connStr;

    public Db(string dbPath)
    {
        // journal WAL desactive: exFAT + cle USB, on garde un seul fichier propre et portable
        _connStr = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connStr);
        c.Open();
        return c;
    }

    public void Init()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS machines (
    unit_id     TEXT PRIMARY KEY,
    legacy_id   TEXT,
    family      TEXT,
    model       TEXT,
    year        TEXT,
    vin         TEXT,
    engine      TEXT,
    engine_vin  TEXT,
    photo_path  TEXT,
    notes       TEXT
);

CREATE TABLE IF NOT EXISTS docs (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    title       TEXT NOT NULL,
    rel_path    TEXT NOT NULL UNIQUE,
    ext         TEXT,
    size_bytes  INTEGER,
    system      TEXT,
    is_dup      INTEGER DEFAULT 0
);

-- lien N:N machine <-> doc (un plan General couvre toute une famille)
CREATE TABLE IF NOT EXISTS doc_machine (
    doc_id      INTEGER NOT NULL,
    unit_id     TEXT NOT NULL,
    PRIMARY KEY (doc_id, unit_id)
);

CREATE TABLE IF NOT EXISTS tags (
    doc_id      INTEGER NOT NULL,
    tag         TEXT NOT NULL,
    PRIMARY KEY (doc_id, tag)
);

CREATE TABLE IF NOT EXISTS launcher (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    name        TEXT NOT NULL,
    category    TEXT,
    target      TEXT NOT NULL,
    args        TEXT,
    working_dir TEXT,
    kind        TEXT DEFAULT 'gui',
    icon_path   TEXT,
    try_embed   INTEGER DEFAULT 0
);

-- meta: flag d'import initial, version de schema
CREATE TABLE IF NOT EXISTS meta (
    key   TEXT PRIMARY KEY,
    value TEXT
);

-- recherche plein-texte sur docs (titre + chemin + systeme)
CREATE VIRTUAL TABLE IF NOT EXISTS docs_fts USING fts5(
    title, rel_path, system,
    content='docs', content_rowid='id'
);

CREATE TRIGGER IF NOT EXISTS docs_ai AFTER INSERT ON docs BEGIN
    INSERT INTO docs_fts(rowid, title, rel_path, system)
    VALUES (new.id, new.title, new.rel_path, new.system);
END;
CREATE TRIGGER IF NOT EXISTS docs_ad AFTER DELETE ON docs BEGIN
    INSERT INTO docs_fts(docs_fts, rowid, title, rel_path, system)
    VALUES ('delete', old.id, old.title, old.rel_path, old.system);
END;
CREATE TRIGGER IF NOT EXISTS docs_au AFTER UPDATE ON docs BEGIN
    INSERT INTO docs_fts(docs_fts, rowid, title, rel_path, system)
    VALUES ('delete', old.id, old.title, old.rel_path, old.system);
    INSERT INTO docs_fts(rowid, title, rel_path, system)
    VALUES (new.id, new.title, new.rel_path, new.system);
END;

-- machines retirees par Julien: l'import auto du json ne doit pas les recreer
CREATE TABLE IF NOT EXISTS deleted_machines (
    unit_id     TEXT PRIMARY KEY
);

-- dossiers virtuels par machine (permet un dossier vide qu'on remplit ensuite)
CREATE TABLE IF NOT EXISTS folders (
    unit_id     TEXT NOT NULL,
    name        TEXT NOT NULL,
    sort        INTEGER DEFAULT 0,
    PRIMARY KEY (unit_id, name)
);
";
        cmd.ExecuteNonQuery();

        // migration: colonnes d'override par machine sur doc_machine (folder + titre local).
        // idempotent: on ignore l'erreur si la colonne existe deja (pas de IF NOT EXISTS sur ADD COLUMN en SQLite)
        AddColumnIfMissing(c, "doc_machine", "folder", "TEXT");
        AddColumnIfMissing(c, "doc_machine", "title_override", "TEXT");
    }

    private static void AddColumnIfMissing(SqliteConnection c, string table, string col, string type)
    {
        using (var check = c.CreateCommand())
        {
            check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name=$col";
            check.Parameters.AddWithValue("$col", col);
            if (Convert.ToInt64(check.ExecuteScalar()) > 0) return;
        }
        using var add = c.CreateCommand();
        add.CommandText = $"ALTER TABLE {table} ADD COLUMN {col} {type}";
        add.ExecuteNonQuery();
    }

    public string? GetMeta(string key)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key=$k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SetMeta(string key, string value)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO meta(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=$v";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    public void UpsertMachine(SqliteConnection c, Machine m)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
INSERT INTO machines(unit_id,legacy_id,family,model,year,vin,engine,engine_vin,photo_path,notes)
VALUES($u,$l,$f,$mo,$y,$v,$e,$ev,$p,$n)
ON CONFLICT(unit_id) DO UPDATE SET
    legacy_id=COALESCE(excluded.legacy_id,legacy_id),
    family=COALESCE(excluded.family,family),
    model=COALESCE(excluded.model,model),
    year=COALESCE(excluded.year,year),
    vin=COALESCE(excluded.vin,vin),
    engine=COALESCE(excluded.engine,engine),
    engine_vin=COALESCE(excluded.engine_vin,engine_vin),
    photo_path=COALESCE(excluded.photo_path,photo_path),
    notes=COALESCE(excluded.notes,notes)";
        cmd.Parameters.AddWithValue("$u", m.UnitId);
        cmd.Parameters.AddWithValue("$l", (object?)m.LegacyId ?? System.DBNull.Value);
        cmd.Parameters.AddWithValue("$f", m.Family);
        cmd.Parameters.AddWithValue("$mo", m.Model);
        cmd.Parameters.AddWithValue("$y", (object?)m.Year ?? System.DBNull.Value);
        cmd.Parameters.AddWithValue("$v", (object?)m.Vin ?? System.DBNull.Value);
        cmd.Parameters.AddWithValue("$e", (object?)m.Engine ?? System.DBNull.Value);
        cmd.Parameters.AddWithValue("$ev", (object?)m.EngineVin ?? System.DBNull.Value);
        cmd.Parameters.AddWithValue("$p", (object?)m.PhotoPath ?? System.DBNull.Value);
        cmd.Parameters.AddWithValue("$n", (object?)m.Notes ?? System.DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public bool MachineExists(string unitId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM machines WHERE unit_id=$u";
        cmd.Parameters.AddWithValue("$u", unitId);
        return cmd.ExecuteScalar() != null;
    }

    public void UpsertMachine(Machine m)
    {
        using var c = Open();
        UpsertMachine(c, m);
    }

    // supprime la machine + ses liens/dossiers. Les docs (fichiers et table docs) restent: partages entre machines.
    public void DeleteMachine(string unitId)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        foreach (var sql in new[] {
            "DELETE FROM doc_machine WHERE unit_id=$u",
            "DELETE FROM folders WHERE unit_id=$u",
            "DELETE FROM machines WHERE unit_id=$u" })
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$u", unitId);
            cmd.ExecuteNonQuery();
        }
        // la machine ne doit pas revenir au prochain boot via l'import auto du json
        using (var del = c.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "INSERT OR IGNORE INTO deleted_machines(unit_id) VALUES($u)";
            del.Parameters.AddWithValue("$u", unitId);
            del.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public HashSet<string> GetDeletedMachines()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT unit_id FROM deleted_machines";
        using var r = cmd.ExecuteReader();
        while (r.Read()) set.Add(r.GetString(0));
        return set;
    }

    public void UndeleteMachine(string unitId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM deleted_machines WHERE unit_id=$u";
        cmd.Parameters.AddWithValue("$u", unitId);
        cmd.ExecuteNonQuery();
    }

    public List<Machine> GetMachines()
    {
        var list = new List<Machine>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT unit_id,legacy_id,family,model,year,vin,engine,engine_vin,photo_path,notes FROM machines ORDER BY family,unit_id";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Machine
            {
                UnitId = r.GetString(0),
                LegacyId = r.IsDBNull(1) ? null : r.GetString(1),
                Family = r.IsDBNull(2) ? "" : r.GetString(2),
                Model = r.IsDBNull(3) ? "" : r.GetString(3),
                Year = r.IsDBNull(4) ? null : r.GetString(4),
                Vin = r.IsDBNull(5) ? null : r.GetString(5),
                Engine = r.IsDBNull(6) ? null : r.GetString(6),
                EngineVin = r.IsDBNull(7) ? null : r.GetString(7),
                PhotoPath = r.IsDBNull(8) ? null : r.GetString(8),
                Notes = r.IsDBNull(9) ? null : r.GetString(9)
            });
        }
        return list;
    }

    public List<DocItem> GetDocsForMachine(string unitId)
    {
        var list = new List<DocItem>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        // folder effectif = override machine, sinon system global, sinon 'Autre'
        // titre effectif = override machine, sinon titre global
        cmd.CommandText = @"
SELECT d.id,
       COALESCE(NULLIF(dm.title_override,''), d.title) AS title,
       d.rel_path, d.ext, d.size_bytes,
       COALESCE(NULLIF(dm.folder,''), d.system) AS folder,
       d.is_dup
FROM docs d JOIN doc_machine dm ON dm.doc_id=d.id
WHERE dm.unit_id=$u
ORDER BY folder, title";
        cmd.Parameters.AddWithValue("$u", unitId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var doc = ReadDoc(r);
            doc.Folder = r.IsDBNull(5) ? null : r.GetString(5);
            list.Add(doc);
        }
        return list;
    }

    // dossiers a afficher pour une machine = union des folders utilises par ses docs
    // + les dossiers vides crees explicitement dans la table folders
    public List<string> GetFoldersForMachine(string unitId)
    {
        var set = new List<string>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT DISTINCT name FROM (
    SELECT COALESCE(NULLIF(dm.folder,''), d.system, 'Autre') AS name
    FROM docs d JOIN doc_machine dm ON dm.doc_id=d.id WHERE dm.unit_id=$u
    UNION
    SELECT name FROM folders WHERE unit_id=$u
) ORDER BY name";
        cmd.Parameters.AddWithValue("$u", unitId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) if (!r.IsDBNull(0)) set.Add(r.GetString(0));
        return set;
    }

    public void CreateFolder(string unitId, string name)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO folders(unit_id,name) VALUES($u,$n)";
        cmd.Parameters.AddWithValue("$u", unitId);
        cmd.Parameters.AddWithValue("$n", name);
        cmd.ExecuteNonQuery();
    }

    // renomme un dossier pour CETTE machine seulement, RECURSIF: "A" -> "X" deplace aussi "A/B/C" en "X/B/C".
    // Reaffecte les folders des docs (y compris herites du system global) + met a jour la table folders.
    public void RenameFolder(string unitId, string oldName, string newName)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        using (var up = c.CreateCommand())
        {
            up.Transaction = tx;
            up.CommandText = @"
UPDATE doc_machine SET folder = $new || substr(eff, length($old)+1)
FROM (
    SELECT dm.doc_id AS did, COALESCE(NULLIF(dm.folder,''), d.system, 'Autre') AS eff
    FROM docs d JOIN doc_machine dm ON dm.doc_id=d.id WHERE dm.unit_id=$u
) s
WHERE doc_machine.unit_id=$u AND doc_machine.doc_id=s.did
  AND (s.eff=$old OR s.eff LIKE $old || '/%')";
            up.Parameters.AddWithValue("$new", newName);
            up.Parameters.AddWithValue("$u", unitId);
            up.Parameters.AddWithValue("$old", oldName);
            up.ExecuteNonQuery();
        }
        using (var f = c.CreateCommand())
        {
            f.Transaction = tx;
            f.CommandText = @"
UPDATE OR IGNORE folders SET name = $new || substr(name, length($old)+1)
WHERE unit_id=$u AND (name=$old OR name LIKE $old || '/%');
INSERT OR IGNORE INTO folders(unit_id,name) VALUES($u,$new)";
            f.Parameters.AddWithValue("$u", unitId);
            f.Parameters.AddWithValue("$old", oldName);
            f.Parameters.AddWithValue("$new", newName);
            f.ExecuteNonQuery();
        }
        tx.Commit();
    }

    // supprime un dossier ET ses sous-dossiers: delie leurs docs de la machine (les fichiers restent)
    public void DeleteFolder(string unitId, string name)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        using (var del = c.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = @"
DELETE FROM doc_machine
WHERE unit_id=$u AND doc_id IN (
    SELECT d.id FROM docs d JOIN doc_machine dm ON dm.doc_id=d.id
    WHERE dm.unit_id=$u AND (COALESCE(NULLIF(dm.folder,''), d.system, 'Autre')=$n
                          OR COALESCE(NULLIF(dm.folder,''), d.system, 'Autre') LIKE $n || '/%')
)";
            del.Parameters.AddWithValue("$u", unitId);
            del.Parameters.AddWithValue("$n", name);
            del.ExecuteNonQuery();
        }
        using (var f = c.CreateCommand())
        {
            f.Transaction = tx;
            f.CommandText = "DELETE FROM folders WHERE unit_id=$u AND (name=$n OR name LIKE $n || '/%')";
            f.Parameters.AddWithValue("$u", unitId);
            f.Parameters.AddWithValue("$n", name);
            f.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void MoveDocToFolder(long docId, string unitId, string folder)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE doc_machine SET folder=$f WHERE doc_id=$d AND unit_id=$u";
        cmd.Parameters.AddWithValue("$f", folder);
        cmd.Parameters.AddWithValue("$d", docId);
        cmd.Parameters.AddWithValue("$u", unitId);
        cmd.ExecuteNonQuery();
    }

    // renomme un doc pour CETTE machine (override local, titre global intact)
    public void RenameDocOnMachine(long docId, string unitId, string newTitle)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE doc_machine SET title_override=$t WHERE doc_id=$d AND unit_id=$u";
        cmd.Parameters.AddWithValue("$t", newTitle);
        cmd.Parameters.AddWithValue("$d", docId);
        cmd.Parameters.AddWithValue("$u", unitId);
        cmd.ExecuteNonQuery();
    }

    // cree le doc (ou retourne l'id existant si le meme rel_path est deja indexe)
    public long InsertDoc(string title, string relPath, string ext, long size, string? system = null)
    {
        using var c = Open();
        using (var q = c.CreateCommand())
        {
            q.CommandText = "SELECT id FROM docs WHERE rel_path=$p";
            q.Parameters.AddWithValue("$p", relPath);
            if (q.ExecuteScalar() is long existing) return existing;
        }
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"INSERT INTO docs(title,rel_path,ext,size_bytes,system) VALUES($t,$p,$e,$s,$sys);
SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$t", title);
        cmd.Parameters.AddWithValue("$p", relPath);
        cmd.Parameters.AddWithValue("$e", ext);
        cmd.Parameters.AddWithValue("$s", size);
        cmd.Parameters.AddWithValue("$sys", (object?)system ?? DBNull.Value);
        return (long)cmd.ExecuteScalar()!;
    }

    public void AddDocToMachine(long docId, string unitId, string? folder)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO doc_machine(doc_id,unit_id,folder) VALUES($d,$u,$f)";
        cmd.Parameters.AddWithValue("$d", docId);
        cmd.Parameters.AddWithValue("$u", unitId);
        cmd.Parameters.AddWithValue("$f", (object?)folder ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void RemoveDocFromMachine(long docId, string unitId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM doc_machine WHERE doc_id=$d AND unit_id=$u";
        cmd.Parameters.AddWithValue("$d", docId);
        cmd.Parameters.AddWithValue("$u", unitId);
        cmd.ExecuteNonQuery();
    }

    // recherche de docs PAS encore lies a la machine, pour le dialog d'ajout
    public List<DocItem> SearchDocsNotOnMachine(string unitId, string query)
    {
        var list = new List<DocItem>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        if (string.IsNullOrWhiteSpace(query))
        {
            cmd.CommandText = @"
SELECT d.id,d.title,d.rel_path,d.ext,d.size_bytes,d.system,d.is_dup
FROM docs d
WHERE d.id NOT IN (SELECT doc_id FROM doc_machine WHERE unit_id=$u)
ORDER BY d.title LIMIT 200";
            cmd.Parameters.AddWithValue("$u", unitId);
        }
        else
        {
            cmd.CommandText = @"
SELECT d.id,d.title,d.rel_path,d.ext,d.size_bytes,d.system,d.is_dup
FROM docs_fts f JOIN docs d ON d.id=f.rowid
WHERE docs_fts MATCH $q
  AND d.id NOT IN (SELECT doc_id FROM doc_machine WHERE unit_id=$u)
ORDER BY rank LIMIT 200";
            cmd.Parameters.AddWithValue("$q", query);
            cmd.Parameters.AddWithValue("$u", unitId);
        }
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadDoc(r));
        return list;
    }

    public List<DocItem> SearchDocs(string query)
    {
        var list = new List<DocItem>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT d.id,d.title,d.rel_path,d.ext,d.size_bytes,d.system,d.is_dup
FROM docs_fts f JOIN docs d ON d.id=f.rowid
WHERE docs_fts MATCH $q ORDER BY rank LIMIT 200";
        cmd.Parameters.AddWithValue("$q", query);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadDoc(r));
        return list;
    }

    public List<LauncherEntry> GetLaunchers()
    {
        var list = new List<LauncherEntry>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,name,category,target,args,working_dir,kind,icon_path,try_embed FROM launcher ORDER BY category,name";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new LauncherEntry
            {
                Id = r.GetInt64(0),
                Name = r.GetString(1),
                Category = r.IsDBNull(2) ? "" : r.GetString(2),
                Target = FromPortable(r.GetString(3)),
                Args = r.IsDBNull(4) ? null : r.GetString(4),
                WorkingDir = r.IsDBNull(5) ? null : FromPortable(r.GetString(5)),
                Kind = r.IsDBNull(6) ? "gui" : r.GetString(6),
                IconPath = r.IsDBNull(7) ? null : r.GetString(7),
                TryEmbed = !r.IsDBNull(8) && r.GetInt64(8) == 1
            });
        return list;
    }

    // portabilite cle USB: tout chemin sous files\ est stocke RELATIF (la lettre de lecteur change d'un PC a l'autre)
    public static string ToPortable(string? path)
    {
        if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path)) return path ?? "";
        var root = App.FilesRoot.TrimEnd('\\') + "\\";
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path.Substring(root.Length) : path;
    }

    public static string FromPortable(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        return Path.IsPathRooted(path) ? path : Path.Combine(App.FilesRoot, path);
    }

    // migration one-shot: entrees launcher creees avec un chemin absolu de l'ancienne lettre
    public void RelativizeLaunchers()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,target,working_dir,icon_path FROM launcher";
        var updates = new List<(long id, string t, string? w, string? i)>();
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                var t = r.GetString(1); var w = r.IsDBNull(2) ? null : r.GetString(2); var i = r.IsDBNull(3) ? null : r.GetString(3);
                var (nt, nw, ni) = (Relax(t), w == null ? null : Relax(w), i == null ? null : Relax(i));
                if (nt != t || nw != w || ni != i) updates.Add((r.GetInt64(0), nt, nw, ni));
            }
        foreach (var u in updates)
        {
            using var up = c.CreateCommand();
            up.CommandText = "UPDATE launcher SET target=$t, working_dir=$w, icon_path=$i WHERE id=$id";
            up.Parameters.AddWithValue("$t", u.t);
            up.Parameters.AddWithValue("$w", (object?)u.w ?? DBNull.Value);
            up.Parameters.AddWithValue("$i", (object?)u.i ?? DBNull.Value);
            up.Parameters.AddWithValue("$id", u.id);
            up.ExecuteNonQuery();
        }
    }

    // "X:\files\programmes\a.exe" -> "programmes\a.exe" quelle que soit la lettre X
    private static string Relax(string p)
    {
        var portable = ToPortable(p);
        if (portable != p) return portable;
        var idx = p.IndexOf("\\files\\", StringComparison.OrdinalIgnoreCase);
        if (Path.IsPathRooted(p) && idx == 2) return p.Substring(idx + 7);   // "G:\files\..." d'une autre lettre
        return p;
    }

    public long AddLauncher(LauncherEntry e)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"INSERT INTO launcher(name,category,target,args,working_dir,kind,icon_path,try_embed)
VALUES($n,$c,$t,$a,$w,$k,$i,$e); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$n", e.Name);
        cmd.Parameters.AddWithValue("$c", (object?)e.Category ?? "");
        cmd.Parameters.AddWithValue("$t", ToPortable(e.Target));
        cmd.Parameters.AddWithValue("$a", (object?)e.Args ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$w", string.IsNullOrEmpty(e.WorkingDir) ? DBNull.Value : ToPortable(e.WorkingDir));
        cmd.Parameters.AddWithValue("$k", e.Kind);
        cmd.Parameters.AddWithValue("$i", (object?)e.IconPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$e", e.TryEmbed ? 1 : 0);
        return (long)cmd.ExecuteScalar()!;
    }

    public void DeleteLauncher(long id)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM launcher WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static DocItem ReadDoc(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        Title = r.GetString(1),
        RelPath = r.GetString(2),
        Ext = r.IsDBNull(3) ? "" : r.GetString(3),
        SizeBytes = r.IsDBNull(4) ? 0 : r.GetInt64(4),
        System = r.IsDBNull(5) ? null : r.GetString(5),
        IsDuplicate = !r.IsDBNull(6) && r.GetInt64(6) == 1
    };
}
