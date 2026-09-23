using System;
using System.IO;
using System.Linq;
using IsotopeApp.Analysis;
using IsotopeApp.Cases;
using IsotopeApp.Storage;

class Program
{
    static void Main()
    {
        var root = CaseLibrary.DefaultRoot();
        Console.WriteLine("case root: " + root);
        var db = new Database(Path.Combine(Path.GetTempPath(), $"iso-smoke-{Guid.NewGuid():N}.db"));
        var store = new Store(db);
        store.EnsureMainBranch();
        var svc = new AnalysisService(store);
        var lib = new CaseLibrary(root);
        foreach (var c in lib.LoadAll())
        {
            string tid = "trial-" + c.CaseId[5..7];
            var outc = svc.RunCase(c, "main", tid);
            var r = outc.Result;
            Console.WriteLine($"{c.CaseId}: feas={r.Feasible} hull={r.InsideMeanHull} ident={r.IdentifiabilityAdequate} regions={r.Regions.Count} candidates={r.Candidates.Count} priorInf={r.PriorInfeasible} events={outc.Events.Count + 1}");
            foreach (var d in r.Diagnostics) Console.WriteLine("   - " + d);
        }
        Console.WriteLine("events on main: " + store.ListEvents("main").Count);
        db.Dispose();
    }
}
