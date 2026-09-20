using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using ShopDeck.Data;

namespace ShopDeck.Core;

// Scanne G:\files, cree les docs, tague vers les machines via UnitCodec.
public class Scanner
{
    private readonly Db _db;
    private readonly string _filesRoot;

    public Scanner(Db db, string filesRoot)
    {
        _db = db;
        _filesRoot = filesRoot;
    }

    // classification par mots-cles dans le chemin (le systeme = pourquoi c'est range comme ca)
    private static string? DetectSystem(string relPath)
    {
        var p = relPath.ToLowerInvariant();
        if (p.Contains("schematic") || p.Contains("electric") || p.Contains("kenr") || p.Contains("renr")) return "Schematics";
        if (p.Contains("mems")) return "MEMS";
        if (p.Contains("dss")) return "DSS";
        if (p.Contains("minestar") || p.Contains("hexagon") || p.Contains("terrain")) return "Minestar";
        if (p.Contains("thermastart")) return "Thermastart";
        if (p.Contains("part") || p.Contains("piece")) return "Parts";
        if (p.Contains("ping") || p.Contains("adresse ip") || p.Contains("radio") || p.Contains("switch")) return "Reseau";
        if (p.Contains("cabine") || p.Contains("remote")) return "Remote";
        return null;
    }

    public ScanResult Scan(Action<string>? progress = null)
    {
        var res = new ScanResult();
        if (!Directory.Exists(_filesRoot)) return res;

        using var c = _db.Open();
        using var tx = c.BeginTransaction();

        foreach (var file in Directory.EnumerateFiles(_filesRoot, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(_filesRoot, file);
            var ext = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
            if (ext is "tmp" or "db-journal") continue;

            var fi = new FileInfo(file);
            var title = Path.GetFileNameWithoutExtension(file);
            var system = DetectSystem(rel);
            var isDup = rel.Contains("_DUP", StringComparison.OrdinalIgnoreCase);

            long docId = UpsertDoc(c, tx, title, rel, ext, fi.Length, system, isDup);
            res.DocCount++;

            // lie le doc a toutes les machines detectees dans son chemin
            var units = UnitCodec.ExtractUnits(rel);
            foreach (var u in units)
            {
                LinkDocMachine(c, tx, docId, u);
                res.LinkCount++;
            }
            if (units.Count == 0) res.Untagged++;

            if (res.DocCount % 500 == 0) progress?.Invoke($"{res.DocCount} fichiers...");
        }

        tx.Commit();
        _db.SetMeta("last_scan", DateTime.Now.ToString("s"));
        return res;
    }

    private static long UpsertDoc(SqliteConnection c, SqliteTransaction tx,
        string title, string rel, string ext, long size, string? system, bool isDup)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO docs(title,rel_path,ext,size_bytes,system,is_dup)
VALUES($t,$r,$e,$s,$sy,$d)
ON CONFLICT(rel_path) DO UPDATE SET
    title=excluded.title, ext=excluded.ext, size_bytes=excluded.size_bytes,
    system=excluded.system, is_dup=excluded.is_dup
RETURNING id";
        cmd.Parameters.AddWithValue("$t", title);
        cmd.Parameters.AddWithValue("$r", rel);
        cmd.Parameters.AddWithValue("$e", ext);
        cmd.Parameters.AddWithValue("$s", size);
        cmd.Parameters.AddWithValue("$sy", (object?)system ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$d", isDup ? 1 : 0);
        return (long)cmd.ExecuteScalar()!;
    }

    private static void LinkDocMachine(SqliteConnection c, SqliteTransaction tx, long docId, string unitId)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR IGNORE INTO doc_machine(doc_id,unit_id) VALUES($d,$u)";
        cmd.Parameters.AddWithValue("$d", docId);
        cmd.Parameters.AddWithValue("$u", unitId);
        cmd.ExecuteNonQuery();
    }
}

public class ScanResult
{
    public int DocCount;
    public int LinkCount;
    public int Untagged;
}
