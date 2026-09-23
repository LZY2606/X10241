using IsotopeMixing.Db;
using IsotopeMixing.Engine;
using IsotopeMixing.Storage;

namespace IsotopeMixing.Web;

public sealed class AppServices
{
    public Database Db { get; }
    public Repository Repository { get; }
    public BranchStore Branches { get; }
    public ResultStore Results { get; }
    public DecisionStore Decisions { get; }
    public ArchiveStore Archives { get; }
    public ImportService Importer { get; }
    public InterpretationService Interpretation { get; }
    public ScenarioEngine Engine { get; }
    public DecisionService DecisionService { get; }
    public ReplayRunner Replay { get; }

    public AppServices(string dbPath)
    {
        Db = new Database(dbPath);
        using (var cn = Db.Open())
        {
            Schema.Migrate(cn);
        }
        Repository = new Repository(Db);
        Branches = new BranchStore(Db);
        Results = new ResultStore(Db);
        Decisions = new DecisionStore(Db);
        Archives = new ArchiveStore(Db);
        Importer = new ImportService(Repository);
        Interpretation = new InterpretationService(Repository);
        Engine = new ScenarioEngine(Repository, Branches, Results, Interpretation);
        DecisionService = new DecisionService(Branches, Decisions);
        Replay = new ReplayRunner(Repository, Branches, Engine, DecisionService, Archives);
    }
}
