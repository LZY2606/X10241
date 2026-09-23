namespace IsotopeMix.Core.Models;

/// <summary>
/// 仓库内自带的离线案例包（源组成室）。
/// 包含一条完整叙事所需的全部原始材料与剧本动作。
/// </summary>
public sealed class CasePack
{
    public string PackId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Domain { get; set; } = "stable-isotope-source-composition-chamber";
    public List<CaseRawItem> Records { get; set; } = new();
    public List<CaseScriptStep> Script { get; set; } = new();
}

/// <summary>导入项：稳定客户端身份 + 原始 JSON（逐字保存）。</summary>
public sealed class CaseRawItem
{
    public string ClientId { get; set; } = "";
    public RecordKind Kind { get; set; }
    public string MaterialKey { get; set; } = "";
    public string RawJson { get; set; } = "";
}

public sealed class CaseScriptStep
{
    public string Action { get; set; } = "";     // analyze / reviseParam / reviseSource / decide / conflict ...
    public string Description { get; set; } = "";
    public Dictionary<string, string> Args { get; set; } = new();
}
