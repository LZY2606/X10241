using System;
using System.Collections.Generic;

namespace IsotopeMixing.Core.Domain;

// 维度（同位素体系），如 d13C / d15N。坐标基准（VPDB、Air）随维度登记。
public sealed record DimensionDef(
    string Id,
    string Label,
    string Unit,
    string ReferenceScale,
    int Ordinal);

// 原始测量值与原始文本表示同时保留；无法转换时 Value 为 null、保留诊断，绝不丢弃。
public sealed record MeasuredValue(
    string DimensionId,
    string? Value,
    double? Parsed,
    string? ParseDiagnostic);

public sealed record UnitDeclaration(
    string DimensionId,
    string RawUnit,
    string? NormalizedUnit,
    string? ConversionDiagnostic);

// 端元的一个（可能是多峰的）模式：均值向量与按维度顺序的方差。
public sealed record EndmemberMode(
    string ModeId,
    IReadOnlyList<double?> Mean,
    IReadOnlyList<double> Variance,
    double Weight);

// 端元来源记录：原始文本随解析值一起保留。
public sealed record EndmemberRecord(
    string Id,
    string Label,
    IReadOnlyList<MeasuredValue> Means,
    IReadOnlyList<MeasuredValue> Uncertainties,
    IReadOnlyList<EndmemberMode> Modes,
    string? ProvenanceNote);

// 端元集合的一个版本（分支修订会产生新版本，旧版本继续可读）。
public sealed record EndmemberSetVersion(
    string Id,
    string Label,
    string? ParentVersionId,
    IReadOnlyList<EndmemberRecord> Endmembers,
    IReadOnlyList<string> DimensionOrder,
    DateTimeOffset CreatedAt,
    string ChangeNote);

public sealed record SampleRecord(
    string Id,
    string Label,
    IReadOnlyList<MeasuredValue> Values,
    IReadOnlyList<UnitDeclaration> Units,
    string? ProvenanceNote);

// 测量协方差（仅覆盖被测维度），支持完整非对角（相关误差）。
public sealed record MeasurementCovariance(
    string Id,
    string Label,
    string SampleId,
    IReadOnlyList<string> DimensionOrder,
    double[][] Matrix,
    bool IsCorrelated,
    IReadOnlyList<string>? RawRows);

// 分馏（校正）因子的一个版本：每个端元×每个维度一个因子，单位与原始文本保留。
public sealed record FractionationFactor(
    string EndmemberId,
    string DimensionId,
    string? RawValue,
    double? Parsed,
    string? ParseDiagnostic);

public sealed record FractionationVersion(
    string Id,
    string Label,
    string? ParentVersionId,
    IReadOnlyList<FractionationFactor> Factors,
    DateTimeOffset CreatedAt,
    string ChangeNote);

// 先验假设：区间或 Dirichlet 形参数。区间为空即不限制；不可行先验会被显式标记而非截断。
public sealed record PriorInterval(string EndmemberId, double? Min, double? Max, string RawText);

public sealed record PriorAssumption(
    string Id,
    string Label,
    string Kind,
    IReadOnlyList<PriorInterval> Intervals,
    IReadOnlyDictionary<string, double>? DirichletAlphas,
    string RawRepresentation);

// 一次分析所引用的全部来源（显式依赖）。参数/来源版本变化据此计算失效范围。
public sealed record AnalysisInputs(
    string SampleId,
    string EndmemberSetVersionId,
    string FractionationVersionId,
    string CovarianceId,
    string PriorId);

// 原始字节块：导入的每个文件按内容寻址保存，旧分支引用的原始字节永远可读。
public sealed record RawBlob(
    string ContentSha256,
    byte[] Bytes,
    string MediaType,
    string OriginalFilename);

// 导入清单中的一条材料：身份稳定（UUIDv5，按命名空间+逻辑键），并指向原始字节。
public sealed record SourceMaterial(
    string StableId,
    string Kind,
    string LogicalKey,
    string BlobSha256,
    string RawRepresentation,
    DateTimeOffset ImportedAt,
    IReadOnlyList<string> Diagnostics);
