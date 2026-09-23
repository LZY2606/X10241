using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace IsoMix.Storage;

public sealed class SnapshotBlob
{
    public string Sha { get; set; } = "";
    public string Base64 { get; set; } = "";
    public string MediaType { get; set; } = "";
    public long ByteLength { get; set; }
}

public sealed class SnapshotRecord
{
    public string StableId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string BlobSha { get; set; } = "";
    public long ByteOffset { get; set; }
    public long ByteLength { get; set; }
    public string CreatedAt { get; set; } = "";
}

public sealed class SnapshotVersion
{
    public string VersionId { get; set; } = "";
    public string StableId { get; set; } = "";
    public int VersionNo { get; set; }
    public string PayloadJson { get; set; } = "";
    public string BlobSha { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}

public sealed class SnapshotBranch
{
    public string BranchId { get; set; } = "";
    public string Label { get; set; } = "";
    public string? ParentBranch { get; set; }
    public string? ParentEvent { get; set; }
    public string Basis { get; set; } = "";
    public string? HeadEvent { get; set; }
    public string CreatedAt { get; set; } = "";
    public int Superseded { get; set; }
}

public sealed class SnapshotEvent
{
    public string EventId { get; set; } = "";
    public int Seq { get; set; }
    public string BranchId { get; set; } = "";
    public string? AtEvent { get; set; }
    public string Type { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public string RuleVersion { get; set; } = "";
    public string DepsJson { get; set; } = "";
    public string SourceBlobsJson { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string? SupersededBy { get; set; }
    public string? SupersededScope { get; set; }
}

public sealed class SnapshotDecision
{
    public string DecisionId { get; set; } = "";
    public string BranchId { get; set; } = "";
    public string EventId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? TargetCandidate { get; set; }
    public string? Rationale { get; set; }
    public string? SupersedesDecision { get; set; }
    public string CreatedAt { get; set; } = "";
}

public sealed class SnapshotArtifact
{
    public string ArtifactId { get; set; } = "";
    public string EventId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}

public sealed class SnapshotInvalidation
{
    public string InvalidationId { get; set; } = "";
    public string DownstreamEvent { get; set; } = "";
    public string ChangedRecord { get; set; } = "";
    public int OldVersion { get; set; }
    public int NewVersion { get; set; }
    public string Scope { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}

public sealed class DatabaseSnapshot
{
    public string Format { get; set; } = "isomix.snapshot/1";
    public string ExportedAt { get; set; } = "";
    public List<SnapshotBlob> Blobs { get; set; } = new();
    public List<SnapshotRecord> Records { get; set; } = new();
    public List<SnapshotVersion> Versions { get; set; } = new();
    public List<SnapshotBranch> Branches { get; set; } = new();
    public List<SnapshotEvent> Events { get; set; } = new();
    public List<SnapshotDecision> Decisions { get; set; } = new();
    public List<SnapshotArtifact> Artifacts { get; set; } = new();
    public List<SnapshotInvalidation> Invalidations { get; set; } = new();
}

public sealed partial class Store
{
    public DatabaseSnapshot ExportSnapshot()
    {
        var snap = new DatabaseSnapshot { ExportedAt = IsoNow() };
        snap.Blobs = QueryAll("SELECT sha256,bytes,media_type,byte_length,first_seen FROM blobs ORDER BY first_seen,sha256;",
            r => new SnapshotBlob
            {
                Sha = r.GetString(0),
                Base64 = Convert.ToBase64String((byte[])r.GetValue(1)),
                MediaType = r.GetString(2),
                ByteLength = r.GetInt64(3)
            });
        snap.Records = QueryAll("SELECT stable_id,kind,blob_sha,byte_offset,byte_length,created_at FROM raw_records ORDER BY stable_id;",
            r => new SnapshotRecord
            {
                StableId = r.GetString(0), Kind = r.GetString(1), BlobSha = r.GetString(2),
                ByteOffset = r.GetInt64(3), ByteLength = r.GetInt64(4), CreatedAt = r.GetString(5)
            });
        snap.Versions = QueryAll("SELECT version_id,stable_id,version_no,payload_json,blob_sha,created_at FROM raw_versions ORDER BY stable_id,version_no;",
            r => new SnapshotVersion
            {
                VersionId = r.GetString(0), StableId = r.GetString(1), VersionNo = r.GetInt32(2),
                PayloadJson = r.GetString(3), BlobSha = r.GetString(4), CreatedAt = r.GetString(5)
            });
        snap.Branches = QueryAll("SELECT branch_id,label,parent_branch,parent_event,basis,head_event,created_at,superseded FROM branches ORDER BY created_at;",
            r => new SnapshotBranch
            {
                BranchId = r.GetString(0), Label = r.GetString(1),
                ParentBranch = r.IsDBNull(2) ? null : r.GetString(2),
                ParentEvent = r.IsDBNull(3) ? null : r.GetString(3),
                Basis = r.GetString(4),
                HeadEvent = r.IsDBNull(5) ? null : r.GetString(5),
                CreatedAt = r.GetString(6), Superseded = r.GetInt32(7)
            });
        snap.Events = QueryAll("SELECT event_id,seq,branch_id,at_event,type,payload_json,rule_version,deps_json,source_blobs_json,created_at,superseded_by,superseded_scope FROM events ORDER BY branch_id,seq;",
            r => new SnapshotEvent
            {
                EventId = r.GetString(0), Seq = r.GetInt32(1), BranchId = r.GetString(2),
                AtEvent = r.IsDBNull(3) ? null : r.GetString(3),
                Type = r.GetString(4), PayloadJson = r.GetString(5), RuleVersion = r.GetString(6),
                DepsJson = r.GetString(7), SourceBlobsJson = r.GetString(8), CreatedAt = r.GetString(9),
                SupersededBy = r.IsDBNull(10) ? null : r.GetString(10),
                SupersededScope = r.IsDBNull(11) ? null : r.GetString(11)
            });
        snap.Decisions = QueryAll("SELECT decision_id,branch_id,event_id,kind,target_candidate,rationale,supersedes_decision,created_at FROM decisions ORDER BY created_at;",
            r => new SnapshotDecision
            {
                DecisionId = r.GetString(0), BranchId = r.GetString(1), EventId = r.GetString(2),
                Kind = r.GetString(3),
                TargetCandidate = r.IsDBNull(4) ? null : r.GetString(4),
                Rationale = r.IsDBNull(5) ? null : r.GetString(5),
                SupersedesDecision = r.IsDBNull(6) ? null : r.GetString(6),
                CreatedAt = r.GetString(7)
            });
        snap.Artifacts = QueryAll("SELECT artifact_id,event_id,kind,payload_json,created_at FROM artifacts ORDER BY created_at;",
            r => new SnapshotArtifact
            {
                ArtifactId = r.GetString(0), EventId = r.GetString(1), Kind = r.GetString(2),
                PayloadJson = r.GetString(3), CreatedAt = r.GetString(4)
            });
        snap.Invalidations = QueryAll("SELECT invalidation_id,downstream_event,changed_record,old_version,new_version,scope,created_at FROM invalidations ORDER BY created_at;",
            r => new SnapshotInvalidation
            {
                InvalidationId = r.GetString(0), DownstreamEvent = r.GetString(1),
                ChangedRecord = r.GetString(2), OldVersion = r.GetInt32(3), NewVersion = r.GetInt32(4),
                Scope = r.GetString(5), CreatedAt = r.GetString(6)
            });
        return snap;
    }

    private List<T> QueryAll<T>(string sql, Func<SqliteDataReader, T> map)
    {
        var list = new List<T>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(map(r));
        return list;
    }

    /// <summary>把快照原样写回（目标库应为空）。稳定 id、序号、来源关系全部不变。</summary>
    public static void RestoreSnapshot(Store target, DatabaseSnapshot snap)
    {
        using var tx = target.Conn.BeginTransaction();
        foreach (var b in snap.Blobs)
        {
            using var cmd = target.Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT OR IGNORE INTO blobs(sha256,bytes,media_type,byte_length,first_seen)
                                VALUES(@s,@b,@m,@l,@t);";
            Add(cmd, "@s", b.Sha); Add(cmd, "@b", Convert.FromBase64String(b.Base64));
            Add(cmd, "@m", b.MediaType); Add(cmd, "@l", b.ByteLength); Add(cmd, "@t", Store.IsoNow());
            cmd.ExecuteNonQuery();
        }
        foreach (var x in snap.Records)
        {
            using var cmd = target.Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO raw_records(stable_id,kind,blob_sha,byte_offset,byte_length,created_at)
                                VALUES(@id,@k,@b,@o,@l,@t);";
            Add(cmd, "@id", x.StableId); Add(cmd, "@k", x.Kind); Add(cmd, "@b", x.BlobSha);
            Add(cmd, "@o", x.ByteOffset); Add(cmd, "@l", x.ByteLength); Add(cmd, "@t", x.CreatedAt);
            cmd.ExecuteNonQuery();
        }
        foreach (var x in snap.Versions)
        {
            using var cmd = target.Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO raw_versions(version_id,stable_id,version_no,payload_json,blob_sha,created_at)
                                VALUES(@v,@id,@n,@p,@b,@t);";
            Add(cmd, "@v", x.VersionId); Add(cmd, "@id", x.StableId); Add(cmd, "@n", x.VersionNo);
            Add(cmd, "@p", x.PayloadJson); Add(cmd, "@b", x.BlobSha); Add(cmd, "@t", x.CreatedAt);
            cmd.ExecuteNonQuery();
        }
        foreach (var x in snap.Branches)
        {
            using var cmd = target.Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO branches(branch_id,label,parent_branch,parent_event,basis,head_event,created_at,superseded)
                                VALUES(@b,@l,@pb,@pe,@basis,@h,@t,@s);";
            Add(cmd, "@b", x.BranchId); Add(cmd, "@l", x.Label); Add(cmd, "@pb", x.ParentBranch);
            Add(cmd, "@pe", x.ParentEvent); Add(cmd, "@basis", x.Basis); Add(cmd, "@h", x.HeadEvent);
            Add(cmd, "@t", x.CreatedAt); Add(cmd, "@s", x.Superseded);
            cmd.ExecuteNonQuery();
        }
        foreach (var x in snap.Events)
        {
            using var cmd = target.Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO events(event_id,seq,branch_id,at_event,type,payload_json,rule_version,deps_json,source_blobs_json,created_at,superseded_by,superseded_scope)
                                VALUES(@id,@seq,@b,@at,@type,@p,@rule,@deps,@blobs,@t,@sb,@ss);";
            Add(cmd, "@id", x.EventId); Add(cmd, "@seq", x.Seq); Add(cmd, "@b", x.BranchId);
            Add(cmd, "@at", x.AtEvent); Add(cmd, "@type", x.Type); Add(cmd, "@p", x.PayloadJson);
            Add(cmd, "@rule", x.RuleVersion); Add(cmd, "@deps", x.DepsJson); Add(cmd, "@blobs", x.SourceBlobsJson);
            Add(cmd, "@t", x.CreatedAt); Add(cmd, "@sb", x.SupersededBy); Add(cmd, "@ss", x.SupersededScope);
            cmd.ExecuteNonQuery();
        }
        foreach (var x in snap.Decisions)
        {
            using var cmd = target.Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO decisions(decision_id,branch_id,event_id,kind,target_candidate,rationale,supersedes_decision,created_at)
                                VALUES(@id,@b,@e,@k,@c,@r,@s,@t);";
            Add(cmd, "@id", x.DecisionId); Add(cmd, "@b", x.BranchId); Add(cmd, "@e", x.EventId);
            Add(cmd, "@k", x.Kind); Add(cmd, "@c", x.TargetCandidate); Add(cmd, "@r", x.Rationale);
            Add(cmd, "@s", x.SupersedesDecision); Add(cmd, "@t", x.CreatedAt);
            cmd.ExecuteNonQuery();
        }
        foreach (var x in snap.Artifacts)
        {
            using var cmd = target.Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO artifacts(artifact_id,event_id,kind,payload_json,created_at)
                                VALUES(@id,@e,@k,@p,@t);";
            Add(cmd, "@id", x.ArtifactId); Add(cmd, "@e", x.EventId); Add(cmd, "@k", x.Kind);
            Add(cmd, "@p", x.PayloadJson); Add(cmd, "@t", x.CreatedAt);
            cmd.ExecuteNonQuery();
        }
        foreach (var x in snap.Invalidations)
        {
            using var cmd = target.Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO invalidations(invalidation_id,downstream_event,changed_record,old_version,new_version,scope,created_at)
                                VALUES(@id,@e,@r,@o,@n,@s,@t);";
            Add(cmd, "@id", x.InvalidationId); Add(cmd, "@e", x.DownstreamEvent);
            Add(cmd, "@r", x.ChangedRecord); Add(cmd, "@o", x.OldVersion); Add(cmd, "@n", x.NewVersion);
            Add(cmd, "@s", x.Scope); Add(cmd, "@t", x.CreatedAt);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
}
