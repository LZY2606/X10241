"use strict";
const state = {
  data: null, branchId: null, scenarioId: null, resultId: null,
  compareBranchId: null, showCompare: false, atSeq: null, expectedSeq: null
};
const $ = (id) => document.getElementById(id);

async function api(path, method = "GET", body) {
  const opt = { method, headers: { "Content-Type": "application/json" } };
  if (body !== undefined) opt.body = JSON.stringify(body);
  const res = await fetch(path, opt);
  const text = await res.text();
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { json = text; }
  if (!res.ok) throw Object.assign(new Error("HTTP " + res.status), { status: res.status, body: json });
  return json;
}

async function refresh() {
  state.data = await api("/api/state");
  render();
}

function resultKey(r) { return r.resultId; }

function currentResults() {
  const d = state.data;
  if (!d) return [];
  return d.results.filter(r => r.branchId === state.branchId);
}

function scenarioResults() {
  return currentResults().filter(r => r.scenarioId === state.scenarioId);
}

function selectResult(rid) {
  state.resultId = rid;
  render();
}

function render() {
  const d = state.data;
  const pill = $("statusPill");
  if (!d || d.rawRecords.length === 0) { pill.textContent = "未载入"; pill.className = "pill"; return; }
  pill.textContent = "已载入 · schema v" + d.schemaVersion; pill.className = "pill live";
  $("schemaPill").textContent = "置信水平 " + (d.confidenceLevel * 100).toFixed(0) + "%";

  // branches
  $("branchList").innerHTML = "";
  for (const b of d.branches) {
    const el = document.createElement("div");
    el.className = "card" + (b.branchId === state.branchId ? " active" : "");
    el.innerHTML = `<div>${escapeHtml(b.name)}</div><div class="uid">${b.branchId} · head #${b.headSeq}</div>`;
    el.onclick = () => { state.branchId = b.branchId; state.scenarioId = null; state.resultId = null; state.atSeq = null; render(); };
    $("branchList").appendChild(el);
  }
  if (!state.branchId && d.branches.length) state.branchId = d.branches[0].branchId;

  // compare branch select
  $("compareBranch").innerHTML = "";
  for (const b of d.branches) {
    const o = document.createElement("option");
    o.value = b.branchId; o.textContent = `${b.name} (${b.branchId})`;
    if (b.branchId === state.branchId && d.branches.length > 1) o.value = d.branches.find(x => x.branchId !== state.branchId).branchId;
    $("compareBranch").appendChild(o);
  }

  // scenarios derived from results present on the branch
  const branchResults = currentResults();
  const scenMap = new Map();
  for (const r of branchResults) {
    if (!scenMap.has(r.scenarioId)) scenMap.set(r.scenarioId, r.scenarioLabel);
  }
  $("scenarioList").innerHTML = "";
  for (const [id, label] of scenMap) {
    const rs = branchResults.filter(r => r.scenarioId === id);
    const invalid = rs.every(r => r.invalidated);
    const el = document.createElement("div");
    el.className = "card" + (id === state.scenarioId ? " active" : "");
    const ident = rs.map(r => r.identifiability)[0] || "";
    el.innerHTML = `<div>${escapeHtml(label)} <span class="badge ${statusOf(rs[0])}">${rs[0].status}${invalid ? " (已失效)" : ""}</span></div>
      <div class="uid">${id} · 候选 ${rs.length} · ${ident}</div>`;
    el.onclick = () => { state.scenarioId = id; state.resultId = rs[0].resultId; render(); };
    $("scenarioList").appendChild(el);
  }

  // candidates
  $("candidateList").innerHTML = "";
  for (const r of scenarioResults()) {
    const el = document.createElement("div");
    el.className = "card" + (r.resultId === state.resultId ? " active" : "");
    el.innerHTML = `<div>候选 ${r.resultId.slice(-6)} <span class="badge ${statusOf(r)}">${r.status}</span>${r.invalidated ? '<span class="badge invalidated">失效</span>' : ''}</div>
      <div class="uid">${r.resultId}</div>`;
    el.onclick = () => selectResult(r.resultId);
    $("candidateList").appendChild(el);
  }

  renderEvents(d);
  renderResultPanels(d);
  renderAssessments(d);
  renderProvenance(d);
  renderDecisions(d);

  if (state.branchId) $("expectedSeq").value = (d.branches.find(b => b.branchId === state.branchId)?.headSeq ?? -1) + 1;
}

function statusOf(r) {
  if (r.invalidated) return "invalidated";
  return r.status === "feasible" ? "feasible" : r.status === "infeasible" ? "infeasible" : "underidentified";
}

function renderEvents(d) {
  const evs = d.events.filter(e => e.branchId === state.branchId || state.showCompare && e.branchId === state.compareBranchId);
  evs.sort((a, b) => a.at.localeCompare(b.at));
  $("eventList").innerHTML = "";
  for (const e of evs) {
    const el = document.createElement("div");
    el.className = "card" + (state.atSeq === e.seq && e.branchId === state.branchId ? " active" : "");
    const summary = summarizeEvent(e);
    el.innerHTML = `<div>#${e.seq} <span class="tag">${e.branchId}</span>${e.type}${e.undoOfEventId ? " ↩" : ""}</div><div class="uid">${escapeHtml(summary)}</div><div class="uid">${e.eventId}</div>`;
    el.title = JSON.stringify(e.payload, null, 2);
    el.onclick = () => { state.atSeq = state.atSeq === e.seq ? null : e.seq; timeTravel(); };
    $("eventList").appendChild(el);
  }
}

function summarizeEvent(e) {
  const p = e.payload || {};
  if (e.type === "scenario_evaluated") return `${p.scenarioLabel || p.scenarioId}: ${p.status} (${p.candidateIndex + 1}/${p.candidateCount})`;
  if (e.type === "decision_made") return `${p.verdict} @ ${p.scenarioId} — ${p.rationale || ""}`;
  if (e.type === "decision_revoked") return `撤销 ${p.decisionId}`;
  if (e.type === "dependencies_invalidated") return `失效范围: ${(p.affectedResults || []).length} 个结论（${p.changedSource}）`;
  if (e.type === "source_revised" || e.type === "parameter_revised") return `修订 ${p.scope}: ${p.target}`;
  if (e.type === "counterexample_started") return `反例 ${p.tag}（改 ${p.changedSource}）`;
  return JSON.stringify(p).slice(0, 80);
}

function renderResultPanels(d) {
  const r = d.results.find(x => x.resultId === state.resultId);
  const titleA = r ? `${r.scenarioLabel} · ${r.fracVersionId || "无分馏"} · ${r.endmemberSetId}` : "选择一个候选";
  $("paneATitle").textContent = titleA;
  $("svgA").innerHTML = r ? isotopeSpaceSvg(d, r) : emptyHint("选择左侧场景以查看同位素空间、端元包络与样品");
  $("regionSvg").innerHTML = r ? regionSvg(r) : emptyHint("可行比例区域将在此显示");
  $("residualSvg").innerHTML = r && r.residuals ? residualSvg(r) : emptyHint("残差图");

  if (state.showCompare) {
    const cb = d.results.filter(x => x.branchId === state.compareBranchId && x.scenarioId === state.scenarioId);
    const rb = cb[0];
    $("paneB").classList.remove("hidden");
    document.getElementById("compareGrid").classList.add("two");
    $("paneBTitle").textContent = rb ? `${rb.fracVersionId} · ${rb.endmemberSetId} @ ${rb.branchId}` : "该分支无对应结果";
    $("svgB").innerHTML = rb ? isotopeSpaceSvg(d, rb, true) : emptyHint("该分支/场景尚无结果，可先计算");
  } else {
    $("paneB").classList.add("hidden");
    document.getElementById("compareGrid").classList.remove("two");
  }
}

function renderAssessments(d) {
  const r = d.results.find(x => x.resultId === state.resultId);
  $("assessmentList").innerHTML = "";
  if (!r) return;
  for (const a of r.assessments || []) {
    const el = document.createElement("div");
    el.className = "card " + (a.satisfied ? "satisfied" : "violated");
    el.innerHTML = `<div><strong>${a.code}</strong> ${a.satisfied ? "✓" : "✗"}</div>
      <div>${escapeHtml(a.basis)}</div><div class="uid">evidence: ${escapeHtml(a.evidence || "")}</div>`;
    $("assessmentList").appendChild(el);
  }
}

function renderProvenance(d) {
  const r = d.results.find(x => x.resultId === state.resultId);
  const host = $("provenance");
  host.innerHTML = "";
  if (!r) return;
  api(`/api/provenance/${r.resultId}`).then(p => {
    const el = document.createElement("div");
    el.className = "card";
    const deps = p.dependencies.map(x => `<div class="uid">${x.dependencyKind}: ${x.dependencyUid}</div>`).join("");
    el.innerHTML = `<div><strong>结果身份</strong></div>
      <div class="uid">${r.resultId}</div>
      <div>事件 #${r.createdSeq} ${r.createdEventId}</div>
      <div style="margin-top:6px"><strong>依赖指纹</strong></div>${deps}
      ${r.invalidated ? `<div class="diag-banner">已被 ${r.invalidatedEventId} 标记失效：${escapeHtml(r.invalidationScope || "")}</div>` : ""}`;
    host.innerHTML = ""; host.appendChild(el);
  }).catch(() => {});
}

function renderDecisions(d) {
  const host = $("decisionList");
  host.innerHTML = "";
  const decs = d.decisions.filter(x => x.branchId === state.branchId && (!state.scenarioId || x.scenarioId === state.scenarioId));
  for (const x of decs) {
    const el = document.createElement("div");
    el.className = "card";
    el.innerHTML = `<div>#${x.seq} ${x.verdict.toUpperCase()} ${x.revoked ? "（已撤销）" : ""}</div>
      <div class="uid">${x.decisionId}</div><div>${escapeHtml(x.rationale || "")}</div>
      ${x.revoked ? "" : `<button data-dec="${x.decisionId}" class="revokeBtn">撤销此裁定</button>`}`;
    host.appendChild(el);
  }
  host.querySelectorAll(".revokeBtn").forEach(b => b.onclick = async () => {
    try {
      await api("/api/revoke", "POST", { branchId: state.branchId, decisionId: b.dataset.dec, expectedSeq: expectedSeq() });
      await refresh();
    } catch (e) { showConflict(e.body); }
  });
}

function expectedSeq() {
  const v = parseInt($("expectedSeq").value, 10);
  return Number.isFinite(v) ? v : null;
}

async function timeTravel() {
  if (state.atSeq === null) { await refresh(); return; }
  const view = await api(`/api/branch/${state.branchId}/at/${state.atSeq}`);
  // replace only result visibility
  const d = state.data;
  d.results = d.results.filter(r => r.branchId !== state.branchId).concat(view.results);
  render();
}

const SVGNS = "http://www.w3.org/2000/svg";
function svgEl(tag, attrs = {}, text) {
  const n = document.createElementNS(SVGNS, tag);
  for (const [k, v] of Object.entries(attrs)) n.setAttribute(k, v);
  if (text !== undefined) n.textContent = text;
  return n;
}
function emptyHint(t) { return `<div class="diag-banner">${t}</div>`; }
function escapeHtml(s) { return String(s ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c])); }

function rawEndmemberPositions(d, r) {
  // Extract endmember means from raw records for the result's endmember set.
  const setRec = d.rawRecords.find(x => x.kind === "endmember_set" && x.uid === r.endmemberSetId);
  const emRecs = d.rawRecords.filter(x => x.kind === "endmember");
  const set = setRec ? setRec.raw : null;
  const memberIds = set ? set.members : [];
  const pts = memberIds.map(id => {
    const rec = emRecs.find(e => e.uid === id);
    const em = rec.raw;
    const frac = r.fracVersionId ? d.rawRecords.find(x => x.kind === "fractionation" && x.uid === r.fracVersionId) : null;
    let x = em.means.d13C, y = em.means.d15N;
    if (frac && frac.raw.offsets && frac.raw.offsets[id]) { x += frac.raw.offsets[id].d13C || 0; y += frac.raw.offsets[id].d15N || 0; }
    return { id, label: em.label, x, y, sx: em.sigma?.d13C || 0, sy: em.sigma?.d15N || 0 };
  });
  return pts;
}

function isotopeSpaceSvg(d, r, dim = false) {
  const W = 520, H = 420, pad = 52;
  const ems = rawEndmemberPositions(d, r);
  const sampleRec = d.rawRecords.find(x => x.kind === "sample" && x.uid === r.sampleUid);
  const covRec = d.rawRecords.find(x => x.kind === "covariance" && x.uid === r.covUid);
  const sx = sampleRec?.raw?.values?.d13C ?? null;
  const sy = sampleRec?.raw?.values?.d15N ?? null;

  const xs = ems.map(e => e.x).concat(sx === null ? [] : [sx]);
  const ys = ems.map(e => e.y).concat(sy === null ? [] : [sy]);
  const minX = Math.min(...xs) - 3, maxX = Math.max(...xs) + 3;
  const minY = Math.min(...ys) - 3, maxY = Math.max(...ys) + 3;
  const px = v => pad + (v - minX) / (maxX - minX) * (W - 2 * pad);
  const py = v => H - pad - (v - minY) / (maxY - minY) * (H - 2 * pad);

  const svg = svgEl("svg", { viewBox: `0 0 ${W} ${H}`, class: dim ? "dim" : "" });
  svg.appendChild(svgEl("rect", { x: 0, y: 0, width: W, height: H, fill: "#11181f", rx: 8 }));
  for (let g = 0; g <= 5; g++) {
    const gx = pad + g * (W - 2 * pad) / 5, gy = pad + g * (H - 2 * pad) / 5;
    svg.appendChild(svgEl("line", { x1: gx, y1: pad, x2: gx, y2: H - pad, stroke: "#22303c" }));
    svg.appendChild(svgEl("line", { x1: pad, y1: gy, x2: W - pad, y2: gy, stroke: "#22303c" }));
  }
  // endmember envelope (convex hull)
  const hull = convexHull(ems.map(e => ({ x: px(e.x), y: py(e.y) })));
  const poly = svgEl("polygon", {
    points: hull.map(p => `${p.x},${p.y}`).join(" "),
    fill: "rgba(79,195,247,.10)", stroke: "#4fc3f7", "stroke-width": 1.5
  });
  svg.appendChild(poly);
  // endmember uncertainty ellipses
  for (const e of ems) {
    svg.appendChild(ellipseApprox(px(e.x), py(e.y), Math.max(6, e.sx * 12), Math.max(6, e.sy * 12), "rgba(79,195,247,.25)", "#4fc3f7"));
    svg.appendChild(svgEl("circle", { cx: px(e.x), cy: py(e.y), r: 5, fill: "#4fc3f7" }));
    const t = svgEl("text", { x: px(e.x) + 8, y: py(e.y) - 8, fill: "#bfe8ff", "font-size": 11 }, e.label);
    svg.appendChild(t);
    svg.appendChild(svgEl("title", {}, `${e.id} δ13C=${e.x.toFixed(2)}‰ VPDB δ15N=${e.y.toFixed(2)}‰ AIR`));
  }
  // sample + measurement covariance ellipse (correlated -> rotated)
  if (sx !== null) {
    let rot = 0, rxv = 10, ryv = 10;
    if (covRec && sy !== null) {
      const m = covRec.raw.matrix;
      // map axes
      const ai = covRec.raw.axes.indexOf("d13C"), aj = covRec.raw.axes.indexOf("d15N");
      const sxx = m[ai][ai], syy = m[aj][aj], sxy = m[ai][aj] || 0;
      const scale = (W - 2 * pad) / (maxX - minX);
      const cov = [[sxx, sxy], [sxy, syy]];
      const { values, vectors } = eigen2(cov);
      for (let k = 0; k < 2; k++) {
        if (Math.abs(vectors[0][k]) + Math.abs(vectors[1][k]) > 0 && values[k] > 0) {
          const len = Math.sqrt(values[k]) * 2.45 * scale;
          if (k === 0) { rxv = Math.abs(vectors[0][k]) * len; ryv = Math.abs(vectors[1][k]) * len; rot = Math.atan2(vectors[1][k], vectors[0][k]) * 180 / Math.PI; }
        }
      }
    }
    if (sy === null) {
      svg.appendChild(svgEl("line", { x1: px(sx), y1: pad, x2: px(sx), y2: H - pad, stroke: "#ffb74d", "stroke-dasharray": "5 4" }));
      svg.appendChild(svgEl("text", { x: px(sx) + 4, y: pad + 12, fill: "#ffb74d", "font-size": 11 }, `${sampleRec.raw.label}（δ15N 缺失，仅一维约束）`));
    } else {
      const e = svgEl("ellipse", {
        cx: px(sx), cy: py(sy), rx: Math.max(8, Math.abs(rxv)), ry: Math.max(8, Math.abs(ryv)),
        fill: "rgba(255,183,77,.15)", stroke: "#ffb74d", "stroke-width": 1.5,
        transform: `rotate(${-rot} ${px(sx)} ${py(sy)})`
      });
      svg.appendChild(e);
      svg.appendChild(svgEl("circle", { cx: px(sx), cy: py(sy), r: 5.5, fill: "#ffb74d", stroke: "#000", "stroke-width": 1 }));
      svg.appendChild(svgEl("text", { x: px(sx) + 8, y: py(sy) + 14, fill: "#ffe0a3", "font-size": 11 }, `${sampleRec.raw.label} (${r.status})`));
    }
  }
  // axis labels
  svg.appendChild(svgEl("text", { x: W / 2, y: H - 14, fill: "#8fa3b5", "font-size": 12, "text-anchor": "middle" }, "δ13C (‰ VPDB)"));
  const yl = svgEl("text", { x: 16, y: H / 2, fill: "#8fa3b5", "font-size": 12, "text-anchor": "middle", transform: `rotate(-90 16 ${H / 2})` }, "δ15N (‰ AIR)");
  svg.appendChild(yl);
  svg.appendChild(svgEl("text", { x: pad, y: 20, fill: "#8fa3b5", "font-size": 10 }, `分馏：${r.fracVersionId || "无"} · 端元集：${r.endmemberSetId} · 候选 ${r.resultId.slice(-6)}${r.invalidated ? " · 已失效" : ""}`));
  return svg;
}

function ellipseApprox(cx, cy, rx, ry, fill, stroke) {
  return svgEl("ellipse", { cx, cy, rx, ry, fill, stroke, "stroke-width": 1 });
}

function eigen2(m) {
  const [a, b] = [m[0][0], m[0][1]], [, c] = [m[1][0], m[1][1]];
  const tr = a + c, det = a * c - b * b;
  const d = Math.sqrt(Math.max(0, tr * tr / 4 - det));
  const l1 = tr / 2 + d, l2 = tr / 2 - d;
  const ang = 0.5 * Math.atan2(2 * b, a - c);
  return { values: [l1, l2], vectors: [[Math.cos(ang), -Math.sin(ang)], [Math.sin(ang), Math.cos(ang)]] };
}

function convexHull(points) {
  if (points.length <= 2) return points;
  const p = [...points].sort((a, b) => a.x - b.x || a.y - b.y);
  const cr = (o, a, b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
  const low = [];
  for (const q of p) { while (low.length >= 2 && cr(low[low.length - 2], low[low.length - 1], q) <= 0) low.pop(); low.push(q); }
  const up = [];
  for (let i = p.length - 1; i >= 0; i--) { const q = p[i]; while (up.length >= 2 && cr(up[up.length - 2], up[up.length - 1], q) <= 0) up.pop(); up.push(q); }
  low.pop(); up.pop();
  return low.concat(up);
}

function regionSvg(r) {
  const W = 520, H = 300, pad = 60;
  const svg = svgEl("svg", { viewBox: `0 0 ${W} ${H}` });
  svg.appendChild(svgEl("rect", { width: W, height: H, fill: "#11181f", rx: 8 }));
  const region = r.region || {};
  const ems = region.endmembers || [];

  if (region.unresolved) {
    const u = region.unresolved;
    svg.appendChild(svgEl("text", { x: pad, y: 40, fill: "#ffb74d", "font-size": 14 }, "⚠ 端元近共线：可识别性不足（比例不可唯一决定）"));
    svg.appendChild(svgEl("text", { x: pad, y: 64, fill: "#8fa3b5", "font-size": 12 }, `零空间方向：${(u.nullDirections || []).map(z => "[" + z + "]").join(" ; ")}`));
    svg.appendChild(svgEl("text", { x: pad, y: 86, fill: "#8fa3b5", "font-size": 12 }, `保留 ${(u.witnessWeights || []).length} 个合法见证权重（全部满足 sum=1 且 w≥0，未做截断归一化）：`));
    (u.witnessWeights || []).forEach((w, i) => {
      svg.appendChild(svgEl("text", { x: pad, y: 110 + i * 20, fill: "#e6edf3", "font-size": 11 },
        `w${i}: [` + w.map(x => x.toFixed(3)).join(", ") + "]  Σ=" + w.reduce((a, b) => a + b, 0).toFixed(4)));
    });
    return svg;
  }
  if (region.line) {
    const line = region.line;
    const p0 = line.points[0], p1 = line.points[line.points.length - 1];
    const minV = Math.min(p0[0], p1[0]) - 0.05, maxV = Math.max(p0[0], p1[0]) + 0.05;
    const x = v => pad + (Math.min(1, Math.max(0, v)) - 0) * (W - 2 * pad);
    const yc = H / 2;
    svg.appendChild(svgEl("line", { x1: x(0), y1: yc, x2: x(1), y2: yc, stroke: "#2c3a47", "stroke-width": 10, "stroke-linecap": "round" }));
    svg.appendChild(svgEl("line", { x1: x(p0[0]), y1: yc, x2: x(p1[0]), y2: yc, stroke: "#4caf7d", "stroke-width": 10, "stroke-linecap": "round" }));
    svg.appendChild(svgEl("text", { x: x(p0[0]), y: yc + 28, fill: "#4caf7d", "font-size": 11, "text-anchor": "middle" }, p0[0].toFixed(3)));
    svg.appendChild(svgEl("text", { x: x(p1[0]), y: yc + 28, fill: "#4caf7d", "font-size": 11, "text-anchor": "middle" }, p1[0].toFixed(3)));
    svg.appendChild(svgEl("text", { x: W / 2, y: 50, fill: "#e6edf3", "font-size": 13, "text-anchor": "middle" }, `可行混合区间：${line.endmemberLabels[0]} 权重 [${p0[0].toFixed(3)}, ${p1[0].toFixed(3)}]`));
    svg.appendChild(svgEl("text", { x: x(0), y: yc - 20, fill: "#8fa3b5", "font-size": 11 }, `纯 ${line.endmemberLabels[1]}`));
    svg.appendChild(svgEl("text", { x: x(1), y: yc - 20, fill: "#8fa3b5", "font-size": 11, "text-anchor": "end" }, `纯 ${line.endmemberLabels[0]}`));
    return svg;
  }
  if (region.polygon) {
    // ternary-ish 2D plot in (w0,w1); w2=1-w0-w1
    const poly = region.polygon;
    const sx = v => pad + v * (W - 2 * pad);
    const sy = v => H - pad - v * (H - 2 * pad);
    // simplex triangle
    const tri = `0,0 1,0 0,1`.split(" ").map(p => { const [a, b] = p.split(",").map(Number); return `${sx(a)},${sy(b)}`; }).join(" ");
    svg.appendChild(svgEl("polygon", { points: tri, fill: "rgba(44,58,71,.25)", stroke: "#3a4d5d" }));
    const pts = poly.vertices.map(v => `${sx(v[0])},${sy(v[1])}`).join(" ");
    svg.appendChild(svgEl("polygon", { points: pts, fill: "rgba(76,175,125,.22)", stroke: "#4caf7d", "stroke-width": 1.5 }));
    if (r.bestWeights) {
      svg.appendChild(svgEl("circle", { cx: sx(r.bestWeights[0]), cy: sy(r.bestWeights[1]), r: 5, fill: "#fff", stroke: "#4caf7d", "stroke-width": 2 }));
    }
    svg.appendChild(svgEl("text", { x: sx(1), y: sy(0) + 18, fill: "#8fa3b5", "font-size": 11, "text-anchor": "end" }, (ems[0]?.label || "w0") + " →"));
    svg.appendChild(svgEl("text", { x: sx(0) - 6, y: sy(1) + 4, fill: "#8fa3b5", "font-size": 11, "text-anchor": "end" }, (ems[1]?.label || "w1") + " ↑"));
    svg.appendChild(svgEl("text", { x: sx(0) - 10, y: sy(0) + 14, fill: "#8fa3b5", "font-size": 11, "text-anchor": "end" }, (ems[2]?.label || "w2") }));
    svg.appendChild(svgEl("text", { x: W / 2, y: 24, fill: "#e6edf3", "font-size": 13, "text-anchor": "middle" },
      `95% 可行比例区域（${poly.vertices.length} 顶点，面积 ${poly.area.toExponential(2)}）；第三坐标 = 1 − w0 − w1`));
    if (r.status === "infeasible")
      svg.appendChild(svgEl("text", { x: W / 2, y: H - 30, fill: "#e57373", "font-size": 13, "text-anchor": "middle" }, "该置信水平下可行区为空：样品超出包络或先验不可行（保留拟合但标记拒绝）"));
    return svg;
  }
  svg.appendChild(svgEl("text", { x: pad, y: H / 2, fill: "#e57373", "font-size": 14 }, "无可刻画区域（见判定依据）"));
  return svg;
}

function residualSvg(r) {
  const res = r.residuals || [];
  if (!res.length) return emptyHint("无残差（可能缺维/不可计算）");
  const W = 520, H = 120, bw = Math.min(120, (W - 80) / res.length);
  const svg = svgEl("svg", { viewBox: `0 0 ${W} ${H}` });
  svg.appendChild(svgEl("rect", { width: W, height: H, fill: "#11181f", rx: 8 }));
  const max = Math.max(1.5, ...res.map(x => Math.abs(x.normalized)));
  const mid = 60;
  const scale = 40 / max;
  svg.appendChild(svgEl("line", { x1: 40, y1: mid, x2: W - 20, y2: mid, stroke: "#3a4d5d" }));
  res.forEach((x, i) => {
    const bx = 60 + i * (bw + 30);
    const h = Math.abs(x.normalized) * scale;
    svg.appendChild(svgEl("rect", {
      x: bx, y: x.normalized >= 0 ? mid - h : mid, width: bw, height: h,
      fill: Math.abs(x.normalized) <= 2 ? "#4caf7d" : "#e57373"
    }));
    svg.appendChild(svgEl("text", { x: bx + bw / 2, y: H - 8, fill: "#8fa3b5", "font-size": 10, "text-anchor": "middle" }, x.axisKey));
    svg.appendChild(svgEl("text", { x: bx + bw / 2, y: x.normalized >= 0 ? mid - h - 4 : mid + h + 12, fill: "#e6edf3", "font-size": 10, "text-anchor": "middle" }, x.normalized.toFixed(2) + "σ"));
  });
  svg.appendChild(svgEl("text", { x: 44, y: 20, fill: "#8fa3b5", "font-size": 11 }, `标准化残差（按测量协方差 σ，含相关传播）  χ²=${r.chi2?.toFixed(2) ?? "n/a"} dof=${r.dof ?? "n/a"} p=${r.pValue?.toFixed(3) ?? "n/a"}`));
  return svg;
}

function showConflict(body) {
  $("conflictModal").classList.remove("hidden");
  $("conflictDiff").textContent = JSON.stringify(body, null, 2);
}

$("conflictClose").onclick = () => { $("conflictModal").classList.add("hidden"); refresh(); };

$("loadBtn").onclick = async () => {
  const r = await api("/api/load-sample", "POST", {});
  if (Array.isArray(r.conflicts)) console.info("replay conflicts recorded:", r.conflicts);
  await refresh();
};

$("replayBtn").onclick = async () => {
  const r = await api("/api/replay", "POST", {});
  console.info("replay", r);
  await refresh();
};

$("compareBtn").onclick = () => {
  state.compareBranchId = $("compareBranch").value;
  state.showCompare = !state.showCompare || state.compareBranchId === state.branchId;
  if (state.compareBranchId === state.branchId) state.showCompare = false;
  render();
};

$("acceptBtn").onclick = async () => {
  if (!state.branchId || !state.scenarioId) return alert("请先选择分支与候选");
  try {
    await api("/api/decide", "POST", {
      branchId: state.branchId, scenarioId: state.scenarioId, resultId: state.resultId,
      verdict: "accept", rationale: $("rationale").value || "页面接受", expectedSeq: expectedSeq()
    });
    await refresh();
  } catch (e) { showConflict(e.body); }
};

$("rejectBtn").onclick = async () => {
  if (!state.branchId || !state.scenarioId) return alert("请先选择分支与候选");
  try {
    await api("/api/decide", "POST", {
      branchId: state.branchId, scenarioId: state.scenarioId, resultId: state.resultId,
      verdict: "reject", rationale: $("rationale").value || "页面否定", expectedSeq: expectedSeq()
    });
    await refresh();
  } catch (e) { showConflict(e.body); }
};

$("reviseFracBtn").onclick = async () => {
  if (!state.branchId || !state.scenarioId) return alert("请先选择分支与场景");
  try {
    const r = await api("/api/revise", "POST", {
      branchId: state.branchId, kind: "parameter", target: "frac-v1",
      payload: { scope: "fractionation", scenario: state.scenarioId, fractionation: "frac-v2" },
      expectedSeq: expectedSeq()
    });
    await api("/api/evaluate", "POST", { branchId: state.branchId, scenarioId: state.scenarioId, expectedSeq: null });
    await refresh();
  } catch (e) { showConflict(e.body); }
};

$("reviseSourceBtn").onclick = async () => {
  if (!state.branchId || !state.scenarioId) return alert("请先选择分支与场景");
  const r0 = state.data.results.find(x => x.resultId === state.resultId);
  try {
    await api("/api/revise", "POST", {
      branchId: state.branchId, kind: "source", target: r0.sampleUid,
      payload: { scope: "local", axis: "d13C", sigma: 1.6, note: "页面发起的局部源数据改动" },
      expectedSeq: expectedSeq()
    });
    await api("/api/evaluate", "POST", { branchId: state.branchId, scenarioId: state.scenarioId });
    await refresh();
  } catch (e) { showConflict(e.body); }
};

$("exportBtn").onclick = async () => {
  const res = await fetch("/api/export");
  const blob = await res.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url; a.download = "isotope-archive.sqlite"; a.click();
  URL.revokeObjectURL(url);
};

$("importFile").onchange = async (ev) => {
  const f = ev.target.files[0];
  if (!f) return;
  const res = await fetch("/api/import-archive", { method: "POST", body: f });
  if (!res.ok) { alert("导入失败：" + await res.text()); return; }
  const info = await res.json();
  alert("导入成功，批次数：" + info.importedBatches + "。刷新视图。");
  await refresh();
};

refresh().catch(() => {});
