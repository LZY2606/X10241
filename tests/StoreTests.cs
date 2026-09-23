using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using IsoMix.App;
using IsoMix.Cases;
using IsoMix.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace IsoMix.Tests;

public sealed class TempDb : IDisposable
{
    public string Path { get; }
    public string Cs { get; }
    public TempDb()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"isomix_{Guid.NewGuid():N}.db");
        Cs = new SqliteConnectionStringBuilder { DataSource = Path, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
    }
    public Store NewStore() => new(Cs);
    public void Dispose() { foreach (var f in Directory.GetFiles(System.IO.Path.GetDirectoryName(Path)!, "isomix_*")) { try { File.Delete(f); } catch { } } }
}

public class StoreTests
{
    private static CasePack Pack() => CaseLoader.LoadEmbedded();

    [Fact]
    public void Import_is_atomic_and_retains_records_and_raw_blobs()
    {
        using var t = new TempDb();
        using var store = t.NewStore();
        var outcome = store.ImportCasePack(Pack());
        Assert.True(outcome.Success);
        Assert.NotEmpty(store.ListRecords("sample"));
        Assert.NotEmpty(store.ListRecords("endmember-set"));
        Assert.NotEmpty(store.ListRecords("covariance"));
        Assert.NotEmpty(store.ListRecords("fractionation"));
        Assert.NotEmpty(store.ListRecords("prior"));
        // 原始字节仍可按 blob 读回
        var v = store.Versions("sample.estuary.s1").Single();
        Assert.NotNull(store.BlobBytes(v.BlobSha));
        Assert.Contains("-21.4", v.PayloadJson);
    }

    [Fact]
    public void Invalid_import_rolls_back_leaving_no_half_records()
    {
        using var t = new TempDb();
        using var store = t.NewStore();
        var bad = Pack();
        bad.Samples[0].Id = ""; // 缺稳定 id
        Assert.ThrowsAny<Exception>(() => store.ImportCasePack(bad));
        Assert.True(store.IsEmpty());
    }

    [Fact]
    public void Re_interpretation_creates_new_branch_old_branch_still_readable()
    {
        using var t = new TempDb();
        using var store = t.NewStore();
        store.ImportCasePack(Pack());
        var head0 = store.GetHead(Store.BaselineBranch);
        string nb = store.CreateBranch("近共线复核", Store.BaselineBranch, "endmember-set-switch");
        Assert.NotEqual(Store.BaselineBranch, nb);
        Assert.Equal(head0, store.GetHead(Store.BaselineBranch));
        Assert.NotEmpty(store.EventsOnBranch(nb));
    }

    [Fact]
    public void Amendment_invalidates_only_dependent_downstream()
    {
        using var t = new TempDb();
        using var store = t.NewStore();
        store.ImportCasePack(Pack());
        var analysis = new AnalysisService(store);
        var head = store.GetHead(Store.BaselineBranch)!;
        analysis.RecordRun(Store.BaselineBranch, head, "hyp.s1.std.none");
        // 记录一次接受裁定
        var head2 = store.GetHead(Store.BaselineBranch)!;
        store.RecordDecision(Store.BaselineBranch, head2, "accept", "candidate:0", "接受海洋占优",
            new() { "sample.estuary.s1" });

        var head3 = store.GetHead(Store.BaselineBranch)!;
        var amended = new SampleSpec
        {
            Id = "sample.estuary.s1",
            Name = "河口悬浮颗粒 S1",
            Values = new()
            {
                ["d13C"] = new RawNumber { Raw = "-21.7", Unit = "per_mil_VPDB" },
                ["d15N"] = new RawNumber { Raw = "7.8", Unit = "per_mil_AIR" }
            },
            Covariance = "cov.estuary.s1"
        };
        var res = store.AmendRecord(Store.BaselineBranch, head3, "sample.estuary.s1", amended, "sample");
        Assert.True(res.Ok);
        Assert.Equal(2, res.NewVersion);
        // 依赖该样品的分析事件应被标记失效
        Assert.Contains(res.InvalidatedEvents, e => store.GetEvent(e)!.Type == EventTypes.AnalysisComputed);
        // 旧版本仍可读
        Assert.Equal(2, store.Versions("sample.estuary.s1").Count);
        Assert.Contains("-21.4", store.Versions("sample.estuary.s1")[0].PayloadJson);
    }

    [Fact]
    public void Two_concurrent_saves_second_sees_conflict_and_diff()
    {
        using var t = new TempDb();
        using var store = t.NewStore();
        store.ImportCasePack(Pack());
        var head = store.GetHead(Store.BaselineBranch)!;
        var first = store.RecordDecision(Store.BaselineBranch, head, "accept", "candidate:0", "窗口A", new() { "x" });
        Assert.True(first.ok);
        var second = store.RecordDecision(Store.BaselineBranch, head, "accept", "candidate:1", "窗口B", new() { "x" });
        Assert.False(second.ok);
        Assert.NotNull(second.conflictWith);
    }

    [Fact]
    public void Decision_can_be_undone_without_deleting_history()
    {
        using var t = new TempDb();
        using var store = t.NewStore();
        store.ImportCasePack(Pack());
        var head = store.GetHead(Store.BaselineBranch)!;
        store.RecordDecision(Store.BaselineBranch, head, "accept", "candidate:0", "x", new() { "x" });
        var head2 = store.GetHead(Store.BaselineBranch)!;
        var undo = store.UndoLastDecision(Store.BaselineBranch, head2);
        Assert.True(undo.ok);
        Assert.Contains(store.EventsOnBranch(Store.BaselineBranch), e => e.Type == EventTypes.DecisionUndone);
    }

    [Fact]
    public void Export_delete_import_preserves_identity_order_and_provenance()
    {
        using var t = new TempDb();
        string snapshotJson;
        int eventCount, versionCount;
        string firstEvent, lastEvent;
        using (var store = t.NewStore())
        {
            store.ImportCasePack(Pack());
            var analysis = new AnalysisService(store);
            analysis.RecordRun(Store.BaselineBranch, store.GetHead(Store.BaselineBranch)!, "hyp.s1.std.none");
            var snap = store.ExportSnapshot();
            eventCount = snap.Events.Count;
            versionCount = snap.Versions.Count;
            firstEvent = snap.Events.First().EventId;
            lastEvent = snap.Events.Last().EventId;
            snapshotJson = JsonSerializer.Serialize(snap, Store.JsonOpts);
        }
        // 删除本地数据库文件
        foreach (var f in new[] { t.Path, t.Path + "-wal", t.Path + "-shm" }) if (File.Exists(f)) File.Delete(f);
        Assert.False(File.Exists(t.Path));

        using var store2 = t.NewStore();
        var snap2 = JsonSerializer.Deserialize<DatabaseSnapshot>(snapshotJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Store.RestoreSnapshot(store2, snap2);

        Assert.Equal(eventCount, store2.EventsOnBranch(Store.BaselineBranch).Count);
        Assert.Equal(versionCount, TotalVersions(store2));
        Assert.Equal(firstEvent, store2.EventsOnBranch(Store.BaselineBranch).First().EventId);
        Assert.Equal(lastEvent, store2.EventsOnBranch(Store.BaselineBranch).Last().EventId);
        // blob 来源关系保持
        var v = store2.Versions("sample.estuary.s1").First();
        Assert.NotNull(store2.BlobBytes(v.BlobSha));
        // 顺序保持
        var seqs = store2.EventsOnBranch(Store.BaselineBranch).Select(e => e.Seq).ToList();
        Assert.Equal(seqs.OrderBy(x => x).ToList(), seqs);
    }

    private static int TotalVersions(Store s)
    {
        int n = 0;
        foreach (var id in s.ListRecords("axis")) n += s.Versions(id).Count;
        foreach (var id in s.ListRecords("sample")) n += s.Versions(id).Count;
        foreach (var id in s.ListRecords("endmember-set")) n += s.Versions(id).Count;
        foreach (var id in s.ListRecords("covariance")) n += s.Versions(id).Count;
        foreach (var id in s.ListRecords("fractionation")) n += s.Versions(id).Count;
        foreach (var id in s.ListRecords("prior")) n += s.Versions(id).Count;
        foreach (var id in s.ListRecords("hypothesis")) n += s.Versions(id).Count;
        foreach (var id in s.ListRecords("scenario")) n += s.Versions(id).Count;
        n += s.Versions("replay").Count;
        return n;
    }
}
