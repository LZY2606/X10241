using System;
using System.Collections.Generic;
using System.Linq;
using IsotopeMixing.Core.Domain;

namespace IsotopeMixing.Core.Engine;

/// <summary>派生层：从原始记录规范化出矩阵；无法转换的值保留诊断，不为画图丢弃。</summary>
public sealed record PreparedInputs(
    string[] ObservedTracers,
    double[] Observed,
    double[,]? SampleCovariance,
    IReadOnlyList<string> EndmemberOrder,
    double[,] EndmemberValues,
    IReadOnlyList<double[,]> EndmemberCovariances,
    IReadOnlyList<Judgment> Diagnostics);

public static class InputPreparer
{
    public static PreparedInputs Prepare(Sample sample, IReadOnlyList<Endmember> endmembers)
    {
        var diagnostics = new List<Judgment>();
        var numericMeasurements = sample.Measurements.Where(m => m.Numeric.HasValue).ToList();
        foreach (var m in sample.Measurements.Where(m => !m.Numeric.HasValue && m.Value.HasValue))
        {
            diagnostics.Add(new Judgment
            {
                Code = JudgmentCodes.UnconvertibleValueKept,
                Severity = "warning",
                Message = $"样品 {sample.SampleId} 的 {m.TracerCode} 值“{m.Value.Text}”无法转换，保留原位，不参与派生计算。",
                Metrics = new Dictionary<string, double?>(),
                RefObjectId = sample.SampleId
            });
        }
        var tracers = numericMeasurements.Select(m => m.TracerCode).ToArray();
        var observed = numericMeasurements.Select(m => m.Numeric!.Value).ToArray();

        var emOrder = endmembers.Select(e => e.EndmemberId).ToList();
        int n = tracers.Length, mc = endmembers.Count;
        var em = new double[mc, n];
        var covs = new List<double[,]>();
        for (int k = 0; k < mc; k++)
        {
            covs.Add(endmembers[k].Covariance is null ? new double[n, n] : SliceCovariance(endmembers[k].Covariance!, tracers));
            for (int j = 0; j < n; j++)
            {
                var pt = endmembers[k].Points.FirstOrDefault(p => p.TracerCode == tracers[j]);
                em[k, j] = pt?.Numeric ?? double.NaN;
            }
        }
        double[,]? sampleCov = sample.Covariance is null ? null : SliceCovariance(sample.Covariance, tracers);
        return new PreparedInputs(tracers, observed, sampleCov, emOrder, em, covs, diagnostics);
    }

    public static double[,] SliceCovariance(CovarianceMatrix matrix, string[] tracers)
    {
        var idx = tracers.Select(t => Array.IndexOf(matrix.TracerCodes, t)).ToArray();
        int n = tracers.Length;
        var result = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                result[i, j] = idx[i] >= 0 && idx[j] >= 0 ? matrix.Values[idx[i], idx[j]] : 0.0;
        return result;
    }
}
