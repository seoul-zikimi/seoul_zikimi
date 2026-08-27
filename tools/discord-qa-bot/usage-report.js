// 토큰 사용량 집계 — Claude Code 세션 로그(~/.claude/projects/**.jsonl)를 파싱해
// "내 세션 vs QA봇" 으로 나눠 리포트 생성. 봇/유저 구분은 세션의 작업 폴더 경로로 함.
const fs = require("fs");
const os = require("os");
const path = require("path");
const readline = require("readline");

const PROJECTS_ROOT = path.join(os.homedir(), ".claude", "projects");

// API 단가 ($/1M tokens). 캐시 쓰기 = 입력×1.25, 캐시 읽기 = 입력×0.1
// 구독(Max 등) 사용 중이면 실제 청구액이 아니라 "API로 환산하면 이 정도" 참고용.
const PRICES = [
  { prefix: "claude-fable-5", in: 10, out: 50 },
  { prefix: "claude-opus", in: 5, out: 25 },
  { prefix: "claude-sonnet", in: 3, out: 15 },
  { prefix: "claude-haiku", in: 1, out: 5 },
];
const FALLBACK_PRICE = { in: 5, out: 25 }; // 모델 불명이면 Opus 단가로 계산

function priceFor(model) {
  if (!model) return FALLBACK_PRICE;
  return PRICES.find((p) => model.startsWith(p.prefix)) || FALLBACK_PRICE;
}

// Claude Code가 프로젝트 경로를 로그 폴더명으로 바꾸는 규칙: 영숫자 외 전부 '-'
function encodeProjectPath(p) {
  return path.resolve(p).replace(/[^a-zA-Z0-9]/g, "-");
}

// 로그 전체를 한 번 파싱해서 메시지 단위 항목으로 반환 (요청 중복은 message.id+requestId로 제거)
async function collectEntries({ userDir, botDir }) {
  const entries = [];
  let dirs = [];
  try {
    dirs = fs.readdirSync(PROJECTS_ROOT).filter((d) => {
      try {
        return fs.statSync(path.join(PROJECTS_ROOT, d)).isDirectory();
      } catch {
        return false;
      }
    });
  } catch {
    return entries; // 로그 폴더 자체가 없으면 빈 리포트
  }

  for (const dir of dirs) {
    const cat = dir === botDir ? "bot" : dir === userDir ? "user" : "other";
    const seen = new Set();
    const full = path.join(PROJECTS_ROOT, dir);
    const files = fs.readdirSync(full).filter((f) => f.endsWith(".jsonl"));
    for (const f of files) {
      const rl = readline.createInterface({
        input: fs.createReadStream(path.join(full, f)),
        crlfDelay: Infinity,
      });
      for await (const line of rl) {
        let j;
        try {
          j = JSON.parse(line);
        } catch {
          continue;
        }
        const u = j.message && j.message.usage;
        if (!u) continue;
        const key = (j.message.id || "") + ":" + (j.requestId || "");
        if (key !== ":" && seen.has(key)) continue;
        seen.add(key);
        entries.push({
          t: j.timestamp ? Date.parse(j.timestamp) : 0,
          cat,
          model: j.message.model || "",
          input: u.input_tokens || 0,
          output: u.output_tokens || 0,
          cacheWrite: u.cache_creation_input_tokens || 0,
          cacheRead: u.cache_read_input_tokens || 0,
        });
      }
    }
  }
  return entries;
}

function emptyTotals() {
  return { input: 0, output: 0, cacheWrite: 0, cacheRead: 0, cost: 0, msgs: 0 };
}

function aggregate(entries, sinceMs, untilMs) {
  const cats = { user: emptyTotals(), bot: emptyTotals(), other: emptyTotals() };
  for (const e of entries) {
    if (e.t < sinceMs || e.t >= untilMs) continue;
    const t = cats[e.cat];
    t.input += e.input;
    t.output += e.output;
    t.cacheWrite += e.cacheWrite;
    t.cacheRead += e.cacheRead;
    t.msgs++;
    const p = priceFor(e.model);
    t.cost +=
      (e.input * p.in +
        e.cacheWrite * p.in * 1.25 +
        e.cacheRead * p.in * 0.1 +
        e.output * p.out) /
      1e6;
  }
  return cats;
}

function fmtTok(n) {
  if (n >= 1e6) return (n / 1e6).toFixed(n >= 1e7 ? 0 : 1) + "M";
  if (n >= 1e3) return (n / 1e3).toFixed(n >= 1e4 ? 0 : 1) + "k";
  return String(n);
}
function fmtUSD(n) {
  return "$" + (n >= 100 ? n.toFixed(0) : n.toFixed(2));
}

function sectionText(label, cats) {
  const total = cats.user.cost + cats.bot.cost + cats.other.cost;
  if (total === 0 && cats.user.msgs + cats.bot.msgs + cats.other.msgs === 0)
    return `**${label}**\n(사용 기록 없음)`;
  const pct = (c) => (total > 0 ? Math.round((c / total) * 100) : 0);
  const row = (emoji, name, t) =>
    `${emoji} ${name} — 출력 ${fmtTok(t.output)} · 캐시읽기 ${fmtTok(t.cacheRead)} · 약 ${fmtUSD(t.cost)} (${pct(t.cost)}%)`;
  const lines = [
    `**${label}**`,
    row("👤", "내 세션", cats.user),
    row("🤖", "QA봇", cats.bot),
  ];
  if (cats.other.msgs > 0) lines.push(row("📁", "기타 프로젝트", cats.other));
  lines.push(`Σ 합계 약 ${fmtUSD(total)}`);
  return lines.join("\n");
}

function dayStart(d) {
  const x = new Date(d);
  x.setHours(0, 0, 0, 0);
  return x.getTime();
}
function dateLabel(ms) {
  const d = new Date(ms);
  return `${String(d.getMonth() + 1).padStart(2, "0")}/${String(d.getDate()).padStart(2, "0")}`;
}

// mode: "today"(슬래시 커맨드용 — 오늘 지금까지) | "yesterday"(매일 자동 리포트용 — 어제 하루)
async function buildReport({ repoRoot, workspace, mode = "today" }) {
  const entries = await collectEntries({
    userDir: encodeProjectPath(repoRoot),
    botDir: encodeProjectPath(workspace),
  });

  const now = Date.now();
  const today0 = dayStart(now);
  const sections = [];

  if (mode === "yesterday") {
    sections.push(
      sectionText(`어제 (${dateLabel(today0 - 1)})`, aggregate(entries, today0 - 86400e3, today0))
    );
  } else {
    sections.push(
      sectionText(`오늘 (${dateLabel(now)}, 지금까지)`, aggregate(entries, today0, now))
    );
  }
  sections.push(
    sectionText("최근 7일", aggregate(entries, today0 - 6 * 86400e3, now)),
    sectionText("전체 누적", aggregate(entries, 0, now))
  );

  return (
    `📊 **토큰 사용량 리포트**\n\n` +
    sections.join("\n\n") +
    `\n\n-# 금액은 API 단가 환산 추정치 (구독 요금제면 실제 청구액 아님 · 캐시읽기 0.1× / 캐시쓰기 1.25× 적용)`
  );
}

module.exports = { buildReport };
