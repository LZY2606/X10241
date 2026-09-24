using System.Text.Json;
using IsotopeMixing.Core;
using Microsoft.Data.Sqlite;

namespace IsotopeMixing.Storage;

public sealed partial class Store
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };
    private readonly Database _db;
    public Store(Database db) => _db = db;

    // ---------- 案例 ----------

    public void OpenCase(string caseId, string title)
    {
        using var cmd = _db.Command(
            "INSERT OR IGNORE INTO cases(case_id,title,opened_at) VALUES($id,$t,$o);");
        cmd.Parameters.AddWithValue("$id", caseId);
        cmd.Parameters.AddWithValue("$t", title);
        cmd.Parameters.AddWithValue("$o", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public bool CaseExists(string caseId)
    {
        using var cmd = _db.Command("SELECT 1 FROM cases WHERE case_id=$id;");
        cmd.Parameters.AddWithValue("$id", caseId);
        return cmd.ExecuteScalar() is not null;
    }

    public string? AnyCase()
    {
        using var cmd = _db.Command("SELECT case_id FROM cases ORDER BY opened_at LIMIT 1;");
        return cmd.ExecuteScalar() as string;
    }

    // ---------- 原始层导入（整体事务，失败回滚，不留半份记录） ----------

    public sealed record ImportUnit(
        RawDocument Document,
        ParsedMaterial Material,
        IReadOnlyList<RawValue> Values,
        EventType EventType,
        string EventSubject,
        JsonElement? EventPayload);

    public void CommitImport(IReadOnlyList<ImportUnit> units, string branchId, string actor,
        string? basisRule = null)
    {
        using var tx = _db.Connection.BeginTransaction();
        try
        {
            foreach (var u in units)
            {
                InsertMaterial(u.Document, u.Material, u.Values, tx);
                string payload = u.EventPayload?.GetRawText() ?? "{}";
                AppendEvent(branchId, u.EventType, u.EventSubject, payload, actor,
                    basisRule: basisRule, tx: tx);
            }
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>失败尝试也只追加一条事件，不写入任何原始/派生数据。</summary>
    public void RecordFailedImport(string branchId, string importRef, string errorJson, string actor)
    {
        using var tx = _db.Connection.BeginTransaction();
        try
        {
            AppendEvent(branchId, EventType.ImportAttemptFailed, importRef, errorJson, actor,
                basisRule: null, tx: tx);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private void InsertMaterial(RawDocument doc, ParsedMaterial mat, IReadOnlyList<RawValue> vals,
        SqliteTransaction tx)
    {
        using (var d = _db.Connection.CreateCommand())
        {
            d.Transaction = tx;
            d.CommandText = """
                INSERT INTO raw_documents(document_id,material_id,material_type,stage,media_type,
                    original_filename,content_bytes,sha256,imported_at,import_ref)
                VALUES($id,$m,$t,$s,$mt,$fn,$b,$h,$at,$ref);
                """;
            d.Parameters.AddWithValue("$id", doc.DocumentId);
            d.Parameters.AddWithValue("$m", doc.MaterialId);
            d.Parameters.AddWithValue("$t", doc.MaterialType);
            d.Parameters.AddWithValue("$s", doc.Stage);
            d.Parameters.AddWithValue("$mt", doc.MediaType);
            d.Parameters.AddWithValue("$fn", doc.OriginalFilename);
            d.Parameters.AddWithValue("$b", doc.ContentBytes);
            d.Parameters.AddWithValue("$h", doc.Sha256);
            d.Parameters.AddWithValue("$at", doc.ImportedAt.ToString("O"));
            d.Parameters.AddWithValue("$ref", doc.ImportRef);
            d.ExecuteNonQuery();
        }
        using (var pm = _db.Connection.CreateCommand())
        {
            pm.Transaction = tx;
            pm.CommandText = """
                INSERT INTO parsed_materials(material_id,kind,label,stage,version_of,created_at,diagnostics)
                VALUES($id,$k,$l,$s,$v,$c,$diag);
                """;
            pm.Parameters.AddWithValue("$id", mat.MaterialId);
            pm.Parameters.AddWithValue("$k", mat.Kind.ToString());
            pm.Parameters.AddWithValue("$l", mat.Label);
            pm.Parameters.AddWithValue("$s", mat.Stage);
            pm.Parameters.AddWithValue("$v", mat.VersionOf);
            pm.Parameters.AddWithValue("$c", mat.CreatedAt.ToString("O"));
            pm.Parameters.AddWithValue("$diag", JsonSerializer.Serialize(mat.Diagnostics));
            pm.ExecuteNonQuery();
        }
        foreach (var v in vals)
        {
            using var rv = _db.Connection.CreateCommand();
            rv.Transaction = tx;
            rv.CommandText = """
                INSERT INTO raw_values(value_id,material_id,record_key,field,raw_text,unit_raw,
                    value,unit_canonical,diagnostic)
                VALUES($id,$m,$rk,$f,$rt,$u,$v,$uc,$diag);
                """;
            rv.Parameters.AddWithValue("$id", v.ValueId);
            rv.Parameters.AddWithValue("$m", v.MaterialId);
            rv.Parameters.AddWithValue("$rk", v.RecordKey);
            rv.Parameters.AddWithValue("$f", v.Field);
            rv.Parameters.AddWithValue("$rt", v.RawText);
            rv.Parameters.AddWithValue("$u", (object?)v.UnitRaw ?? DBNull.Value);
            rv.Parameters.AddWithValue("$v", (object?)v.Value ?? DBNull.Value);
            rv.Parameters.AddWithValue("$uc", (object?)v.UnitCanonical ?? DBNull.Value);
            rv.Parameters.AddWithValue("$diag", (object?)v.Diagnostic ?? DBNull.Value);
            rv.ExecuteNonQuery();
        }
    }

    // ---------- 事件 ----------

    public EventRecord AppendEvent(string branchId, EventType type, string subjectId,
        string payloadJson, string actor, string? basisRule = null,
        string? expectedParentId = null, string? note = null,
        SqliteTransaction? tx = null)
    {
        bool ownTx = tx is null;
        tx ??= _db.Connection.BeginTransaction();
        try
        {
            string? parentId;
            long nextIndex;
            using (var q = _db.Connection.CreateCommand())
            {
                q.Transaction = tx;
                q.CommandText = "SELECT head_event_id FROM branches WHERE branch_id=$b;";
                q.Parameters.AddWithValue("$b", branchId);
                parentId = q.ExecuteScalar() as string;
            }
            if (expectedParentId is not null &&
                !string.Equals(parentId, expectedParentId, StringComparison.Ordinal))
            {
                throw new BranchConflictException(
                    $"分支 {branchId} 头部已变化：期望 {expectedParentId}，实际 {parentId ?? "(空)"}",
                    expectedParentId, parentId);
            }
            using (var q = _db.Connection.CreateCommand())
            {
                q.Transaction = tx;
                q.CommandText = "SELECT COALESCE(MAX(branch_index),0)+1 FROM events WHERE branch_id=$b;";
                q.Parameters.AddWithValue("$b", branchId);
                nextIndex = (long)q.ExecuteScalar()!;
            }

            string eventId = Deterministic.EventId(branchId, nextIndex, type.ToString(), subjectId, payloadJson);
            string now = DateTimeOffset.UtcNow.ToString("O");

            using (var ins = _db.Connection.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText = """
                    INSERT INTO events(event_id,branch_id,branch_index,parent_event_id,type,subject_id,
                        payload_json,occurred_at,actor,basis_rule,snapshot_ref,adjudicator_note)
                    VALUES($e,$b,$i,$p,$t,$s,$pl,$o,$a,$r,$sn,$note);
                    SELECT last_insert_rowid();
                    """;
                ins.Parameters.AddWithValue("$e", eventId);
                ins.Parameters.AddWithValue("$b", branchId);
                ins.Parameters.AddWithValue("$i", nextIndex);
                ins.Parameters.AddWithValue("$p", (object?)parentId ?? DBNull.Value);
                ins.Parameters.AddWithValue("$t", type.ToString());
                ins.Parameters.AddWithValue("$s", subjectId);
                ins.Parameters.AddWithValue("$pl", payloadJson);
                ins.Parameters.AddWithValue("$o", now);
                ins.Parameters.AddWithValue("$a", actor);
                ins.Parameters.AddWithValue("$r", (object?)basisRule ?? DBNull.Value);
                ins.Parameters.AddWithValue("$sn", DBNull.Value);
                ins.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
                long seq = (long)ins.ExecuteScalar()!;

                using var upd = _db.Connection.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = "UPDATE branches SET head_event_id=$e, head_seq=$i WHERE branch_id=$b;";
                upd.Parameters.AddWithValue("$e", eventId);
                upd.Parameters.AddWithValue("$i", nextIndex);
                upd.Parameters.AddWithValue("$b", branchId);
                upd.ExecuteNonQuery();

                if (ownTx) tx.Commit();
                return new EventRecord(seq, eventId, branchId, nextIndex, parentId, type,
                    subjectId, payloadJson, DateTimeOffset.Parse(now), actor, basisRule, null);
            }
        }
        catch
        {
            if (ownTx) tx.Rollback();
            throw;
        }
    }
}

public sealed class BranchConflictException : Exception
{
    public string? ExpectedParent { get; }
    public string? ActualParent { get; }
    public BranchConflictException(string msg, string? expected, string? actual) : base(msg)
    {
        ExpectedParent = expected; ActualParent = actual;
    }
}

public partial class Store
{
    // ---------- 分支 ----------

    public void CreateBranch(BranchInfo info)
    {
        using var cmd = _db.Command("""
            INSERT INTO branches(branch_id,name,parent_branch_id,forked_from_event,created_at,
                created_from,head_event_id,head_seq)
            VALUES($id,$n,$p,$f,$c,$cf,$h,$s);
            """);
        cmd.Parameters.AddWithValue("$id", info.BranchId);
        cmd.Parameters.AddWithValue("$n", info.Name);
        cmd.Parameters.AddWithValue("$p", (object?)info.ParentBranchId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$f", (object?)info.ForkedFromEventId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$c", info.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$cf", info.CreatedFrom);
        cmd.Parameters.AddWithValue("$h", (object?)info.HeadEventId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$s", info.HeadSeq);
        cmd.ExecuteNonQuery();
    }

    public List<BranchInfo> ListBranches()
    {
        using var q = _db.Command(
            "SELECT branch_id,name,parent_branch_id,forked_from_event,created_at,created_from,head_event_id,head_seq FROM branches ORDER BY rowid;");
        using var r = q.ExecuteReader();
        var list = new List<BranchInfo>();
        while (r.Read())
        {
            list.Add(new BranchInfo(
                r.GetString(0), r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                DateTimeOffset.Parse(r.GetString(4)), r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6), r.GetInt64(7)));
        }
        return list;
    }

    public BranchInfo GetBranch(string branchId)
    {
        using var q = _db.Command(
            "SELECT branch_id,name,parent_branch_id,forked_from_event,created_at,created_from,head_event_id,head_seq FROM branches WHERE branch_id=$id;");
        q.Parameters.AddWithValue("$id", branchId);
        using var r = q.ExecuteReader();
        if (!r.Read()) throw new KeyNotFoundException($"分支不存在：{branchId}");
        return new BranchInfo(r.GetString(0), r.GetString(1),
            r.IsDBNull(2) ? null : r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3),
            DateTimeOffset.Parse(r.GetString(4)), r.GetString(5),
            r.IsDBNull(6) ? null : r.GetString(6), r.GetInt64(7));
    }

    // ---------- 事件读取 / 重放 ----------

    public List<EventRecord> ReadEvents(string branchId, long? upToSeq = null)
    {
        using var q = _db.Command(
            "SELECT seq,event_id,branch_id,branch_index,parent_event_id,type,subject_id,payload_json,occurred_at,actor,basis_rule,snapshot_ref,depends_on FROM events WHERE branch_id=$b "
            + (upToSeq is null ? "" : "AND seq<=$up ") + "ORDER BY branch_index;");
        q.Parameters.AddWithValue("$b", branchId);
        if (upToSeq is not null) q.Parameters.AddWithValue("$up", upToSeq.Value);
        using var r = q.ExecuteReader();
        return MapEvents(r);
    }

    public EventRecord? GetEvent(string eventId)
    {
        using var q = _db.Command(
            "SELECT seq,event_id,branch_id,branch_index,parent_event_id,type,subject_id,payload_json,occurred_at,actor,basis_rule,snapshot_ref,depends_on FROM events WHERE event_id=$id;");
        q.Parameters.AddWithValue("$id", eventId);
        using var r = q.ExecuteReader();
        return r.Read() ? MapEvent(r) : null;
    }

    public List<EventRecord> AllEvents()
    {
        using var q = _db.Command(
            "SELECT seq,event_id,branch_id,branch_index,parent_event_id,type,subject_id,payload_json,occurred_at,actor,basis_rule,snapshot_ref,depends_on FROM events ORDER BY seq;");
        using var r = q.ExecuteReader();
        return MapEvents(r);
    }

    private static List<EventRecord> MapEvents(SqliteDataReader r)
    {
        var list = new List<EventRecord>();
        while (r.Read()) list.Add(MapEvent(r));
        return list;
    }

    private static EventRecord MapEvent(SqliteDataReader r)
    {
        var rec = new EventRecord(r.GetInt64(0), r.GetString(1), r.GetString(2),
            r.GetInt64(3), r.IsDBNull(4) ? null : r.GetString(4),
            Enum.Parse<EventType>(r.GetString(5)), r.GetString(6), r.GetString(7),
            DateTimeOffset.Parse(r.GetString(8)), r.GetString(9),
            r.IsDBNull(10) ? null : r.GetString(10),
            r.IsDBNull(11) ? null : r.GetString(11))
        {
            DependsOn = JsonSerializer.Deserialize<long[]>(r.GetString(12)) ?? Array.Empty<long>()
        };
        return rec;
    }

    // ---------- 分析缓存与失效范围 ----------

    public void SaveAnalysis(StoredAnalysis a)
    {
        using var cmd = _db.Command("""
            INSERT OR REPLACE INTO analyses(analysis_id,branch_id,at_event_seq,sample_id,
                endmember_set_id,fractionation_version,priors_id,covariance_id,result_json,
                status_summary,computed_at,depends_material_ids,stale,stale_reason)
            VALUES($id,$b,$s,$sm,$e,$f,$p,$c,$j,$ss,$t,$dep,$st,$r);
            """);
        cmd.Parameters.AddWithValue("$id", a.AnalysisId);
        cmd.Parameters.AddWithValue("$b", a.BranchId);
        cmd.Parameters.AddWithValue("$s", a.AtEventSeq);
        cmd.Parameters.AddWithValue("$sm", a.SampleId);
        cmd.Parameters.AddWithValue("$e", a.EndmemberSetId);
        cmd.Parameters.AddWithValue("$f", a.FractionationVersion);
        cmd.Parameters.AddWithValue("$p", a.PriorsId);
        cmd.Parameters.AddWithValue("$c", a.CovarianceId);
        cmd.Parameters.AddWithValue("$j", a.ResultJson);
        cmd.Parameters.AddWithValue("$ss", a.StatusSummary);
        cmd.Parameters.AddWithValue("$t", a.ComputedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$dep", JsonSerializer.Serialize(a.DependsOnMaterialIds));
        cmd.Parameters.AddWithValue("$st", a.Stale ? 1 : 0);
        cmd.Parameters.AddWithValue("$r", (object?)a.StaleReason ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public List<StoredAnalysis> ListAnalyses(string branchId)
    {
        using var q = _db.Command(
            "SELECT analysis_id,branch_id,at_event_seq,sample_id,endmember_set_id,fractionation_version,priors_id,covariance_id,result_json,status_summary,computed_at,depends_material_ids,stale,stale_reason FROM analyses WHERE branch_id=$b ORDER BY at_event_seq;");
        q.Parameters.AddWithValue("$b", branchId);
        return MapAnalyses(q);
    }

    public StoredAnalysis? FindAnalysis(string branchId, string sampleId)
    {
        using var q = _db.Command(
            "SELECT analysis_id,branch_id,at_event_seq,sample_id,endmember_set_id,fractionation_version,priors_id,covariance_id,result_json,status_summary,computed_at,depends_material_ids,stale,stale_reason FROM analyses WHERE branch_id=$b AND sample_id=$s ORDER BY at_event_seq DESC LIMIT 1;");
        q.Parameters.AddWithValue("$b", branchId);
        q.Parameters.AddWithValue("$s", sampleId);
        using var r = q.ExecuteReader();
        return r.Read() ? MapAnalysis(r) : null;
    }

    private static List<StoredAnalysis> MapAnalyses(SqliteCommand q)
    {
        using var r = q.ExecuteReader();
        var list = new List<StoredAnalysis>();
        while (r.Read()) list.Add(MapAnalysis(r));
        return list;
    }

    private static StoredAnalysis MapAnalysis(SqliteDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetString(3), r.GetString(4),
        r.GetString(5), r.GetString(6), r.GetString(7), r.GetString(8), r.GetString(9),
        DateTimeOffset.Parse(r.GetString(10)))
    {
        DependsOnMaterialIds = JsonSerializer.Deserialize<string[]>(r.GetString(11)) ?? Array.Empty<string>(),
        Stale = r.GetInt32(12) == 1,
        StaleReason = r.IsDBNull(13) ? null : r.GetString(13)
    };

    /// <summary>
    /// 参数/参考版本变化后，只标记依赖被改材料的分析为失效，
    /// 不删除也不全局替换旧结论；返回受影响范围。
    /// </summary>
    public (List<StoredAnalysis> affected, EventRecord invalidationEvent) MarkInvalidated(
        string branchId, IReadOnlySet<string> changedMaterialIds, string newVersionEventId, string actor)
    {
        var all = ListAnalyses(branchId);
        var hit = all.Where(a => a.DependsOnMaterialIds.Any(changedMaterialIds.Contains)).ToList();
        foreach (var a in hit)
        {
            using var cmd = _db.Command(
                "UPDATE analyses SET stale=1, stale_reason=$r WHERE analysis_id=$id;");
            cmd.Parameters.AddWithValue("$id", a.AnalysisId);
            cmd.Parameters.AddWithValue("$r", $"依赖材料变更：{string.Join(",", changedMaterialIds)} → {newVersionEventId}");
            cmd.ExecuteNonQuery();
        }
        var payload = JsonSerializer.Serialize(new
        {
            changedMaterials = changedMaterialIds,
            affectedAnalyses = hit.Select(a => a.AnalysisId),
            unaffectedAnalyses = all.Except(hit).Select(a => a.AnalysisId),
            triggerEvent = newVersionEventId
        }, JsonOpts);
        var ev = AppendEvent(branchId, EventType.InvalidationComputed, "invalidation", payload, actor,
            basisRule: "DEPENDENCY_SCOPED_INVALIDATION");
        return (hit, ev);
    }
}

public partial class Store
{
    // ---------- 原始层读取 ----------

    public List<RawDocument> ListDocuments()
    {
        using var q = _db.Command(
            "SELECT document_id,material_id,material_type,stage,media_type,original_filename,content_bytes,sha256,imported_at,import_ref FROM raw_documents ORDER BY rowid;");
        using var r = q.ExecuteReader();
        var list = new List<RawDocument>();
        while (r.Read())
        {
            list.Add(new RawDocument(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                r.GetString(4), r.GetString(5), (byte[])r[6], r.GetString(7),
                DateTimeOffset.Parse(r.GetString(8)), r.GetString(9)));
        }
        return list;
    }

    public RawDocument? GetDocument(string documentId)
    {
        using var q = _db.Command(
            "SELECT document_id,material_id,material_type,stage,media_type,original_filename,content_bytes,sha256,imported_at,import_ref FROM raw_documents WHERE document_id=$id;");
        q.Parameters.AddWithValue("$id", documentId);
        using var r = q.ExecuteReader();
        if (!r.Read()) return null;
        return new RawDocument(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
            r.GetString(4), r.GetString(5), (byte[])r[6], r.GetString(7),
            DateTimeOffset.Parse(r.GetString(8)), r.GetString(9));
    }

    public List<ParsedMaterial> ListMaterials()
    {
        using var q = _db.Command(
            "SELECT material_id,kind,label,stage,version_of,created_at,diagnostics FROM parsed_materials ORDER BY rowid;");
        using var r = q.ExecuteReader();
        var list = new List<ParsedMaterial>();
        while (r.Read())
        {
            list.Add(new ParsedMaterial(r.GetString(0),
                Enum.Parse<MaterialKind>(r.GetString(1)), r.GetString(2), r.GetString(3),
                r.GetString(4), DateTimeOffset.Parse(r.GetString(5)),
                ListValues(r.GetString(0)),
                System.Text.Json.JsonSerializer.Deserialize<List<string>>(r.GetString(6)) ?? new()));
        }
        return list;
    }

    public ParsedMaterial? GetMaterial(string materialId)
    {
        using var q = _db.Command(
            "SELECT material_id,kind,label,stage,version_of,created_at,diagnostics FROM parsed_materials WHERE material_id=$id;");
        q.Parameters.AddWithValue("$id", materialId);
        using var r = q.ExecuteReader();
        if (!r.Read()) return null;
        return new ParsedMaterial(r.GetString(0),
            Enum.Parse<MaterialKind>(r.GetString(1)), r.GetString(2), r.GetString(3),
            r.GetString(4), DateTimeOffset.Parse(r.GetString(5)),
            ListValues(materialId),
            System.Text.Json.JsonSerializer.Deserialize<List<string>>(r.GetString(6)) ?? new());
    }

    public List<RawValue> ListValues(string materialId)
    {
        using var q = _db.Command(
            "SELECT value_id,material_id,record_key,field,raw_text,unit_raw,value,unit_canonical,diagnostic FROM raw_values WHERE material_id=$m ORDER BY rowid;");
        q.Parameters.AddWithValue("$m", materialId);
        using var r = q.ExecuteReader();
        var list = new List<RawValue>();
        while (r.Read())
            list.Add(new RawValue(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? null : r.GetDouble(6),
                r.IsDBNull(7) ? null : r.GetString(7),
                r.IsDBNull(8) ? null : r.GetString(8)));
        return list;
    }

    public List<RawValue> AllValues()
    {
        using var q = _db.Command(
            "SELECT value_id,material_id,record_key,field,raw_text,unit_raw,value,unit_canonical,diagnostic FROM raw_values ORDER BY rowid;");
        using var r = q.ExecuteReader();
        var list = new List<RawValue>();
        while (r.Read())
            list.Add(new RawValue(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? null : r.GetDouble(6),
                r.IsDBNull(7) ? null : r.GetString(7),
                r.IsDBNull(8) ? null : r.GetString(8)));
        return list;
    }
}
