// Reference receiver for Agent Bridge statistics and error reports (Cloudflare Worker).
// Bindings: KV namespace STATS; secrets GITHUB_TOKEN (fine-grained, Issues: write) and REPO ("owner/name").
// Stores no IPs. Aggregates command counts per day/version; files one GitHub issue per new error signature.

const MAX_BODY = 64 * 1024;

export default {
  async fetch(request, env) {
    if (request.method !== "POST" || new URL(request.url).pathname !== "/v1/report") return new Response("not found", { status: 404 });
    const text = await request.text();
    if (text.length > MAX_BODY) return new Response("too large", { status: 413 });
    let r;
    try { r = JSON.parse(text); } catch { return new Response("bad json", { status: 400 }); }
    if (r.schema !== 1 || typeof r.install !== "string" || !/^[0-9a-f]{32}$/.test(r.install)) return new Response("bad schema", { status: 400 });

    // One report per install per 10 minutes.
    const rateKey = "rate:" + r.install;
    if (await env.STATS.get(rateKey)) return new Response("rate limited", { status: 429 });
    await env.STATS.put(rateKey, "1", { expirationTtl: 600 });

    const day = new Date().toISOString().slice(0, 10);
    const statsKey = `day:${day}:${String(r.mod).slice(0, 16)}`;
    const stats = JSON.parse((await env.STATS.get(statsKey)) || '{"reports":0,"installs":{},"commands":{},"os":{},"runtime":{},"lang":{},"agents":{}}');
    stats.reports++;
    stats.installs[r.install.slice(0, 8)] = 1;
    for (const [k, v] of Object.entries(r.commands || {})) {
      const c = (stats.commands[k] ||= [0, 0]);
      c[0] += +v[0] || 0; c[1] += +v[1] || 0;
    }
    for (const f of ["os", "runtime", "lang"]) if (r[f]) stats[f][String(r[f]).slice(0, 40)] = (stats[f][String(r[f]).slice(0, 40)] || 0) + 1;
    for (const a of r.agents || []) stats.agents[String(a).slice(0, 16)] = (stats.agents[String(a).slice(0, 16)] || 0) + 1;
    await env.STATS.put(statsKey, JSON.stringify(stats), { expirationTtl: 60 * 60 * 24 * 400 });

    for (const e of (r.errors || []).slice(0, 10)) {
      if (!/^[0-9a-f]{16}$/.test(e.sig)) continue;
      const sigKey = "sig:" + e.sig;
      const known = JSON.parse((await env.STATS.get(sigKey)) || "null");
      if (known) { known.count += +e.count || 1; known.installs[r.install.slice(0, 8)] = 1; await env.STATS.put(sigKey, JSON.stringify(known)); continue; }
      const issue = await fileIssue(env, r, e);
      await env.STATS.put(sigKey, JSON.stringify({ issue, count: +e.count || 1, installs: { [r.install.slice(0, 8)]: 1 } }));
    }
    return new Response("ok");
  },
};

async function fileIssue(env, r, e) {
  const body = [
    `Automatic error report \`${e.sig}\` — Agent Bridge ${r.mod}, RimWorld ${r.game}, ${r.runtime}/${r.os}, lang ${r.lang}, harmony ${r.harmony}`,
    `Command: \`${e.cmd}\`, count in first report: ${e.count}`,
    "```", String(e.trace).slice(0, 4000), "```",
  ].join("\n");
  const res = await fetch(`https://api.github.com/repos/${env.REPO}/issues`, {
    method: "POST",
    headers: { authorization: `Bearer ${env.GITHUB_TOKEN}`, "user-agent": "agent-bridge-telemetry", accept: "application/vnd.github+json" },
    body: JSON.stringify({ title: `[auto] ${e.type} in ${e.cmd} (${e.sig})`, body, labels: ["auto-report", "bug"] }),
  });
  return res.ok ? (await res.json()).number : null;
}
