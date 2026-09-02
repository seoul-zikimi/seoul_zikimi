require("dotenv").config();
const {
  Client,
  GatewayIntentBits,
  Partials,
  ChannelType,
  Events,
} = require("discord.js");
const { spawn, execFile } = require("child_process");
const fs = require("fs");
const path = require("path");
const { buildReport } = require("./usage-report");

const TOKEN = process.env.DISCORD_TOKEN;
const FORUM_ID = process.env.QA_FORUM_CHANNEL_ID;
const REPO_ROOT = process.env.REPO_ROOT;
const CLAUDE_CMD = process.env.CLAUDE_CMD || "claude";
const REMOTE = process.env.GIT_REMOTE || "origin";
const MAIN_BRANCH = process.env.MAIN_BRANCH || "main";
const CREATE_PR = (process.env.CREATE_PR || "true") === "true";
// 봇 전용 작업 폴더 (git worktree) — 사용자의 작업 폴더를 건드리지 않기 위함
const WORKSPACE =
  process.env.BOT_WORKSPACE ||
  path.join(path.dirname(REPO_ROOT || "."), path.basename(REPO_ROOT || "repo") + "-qa-workspace");

// 토큰 리포트: 채널 ID 넣으면 매일 TOKEN_REPORT_HOUR시에 자동 보고 (안 넣으면 /tokenusage만 동작)
const REPORT_CHANNEL_ID = process.env.TOKEN_REPORT_CHANNEL_ID || "";
const REPORT_HOUR = parseInt(process.env.TOKEN_REPORT_HOUR || "10", 10);

const EMOJI_OK = "✅";
const EMOJI_NO = "❌";
const PLAN_TIMEOUT_MS = 10 * 60 * 1000;
const FIX_TIMEOUT_MS = 30 * 60 * 1000;

// 포럼에 이 이름의 태그가 있으면 봇이 자동으로 갱신함 (없어도 동작에 지장 없음)
const TAG_NAMES = { waiting: "대기", fixing: "수정중", done: ["해결", "완료"] };
const NOTICE_TAG = "공지"; // 이 태그 붙은 글은 봇이 완전히 무시
const UNRESOLVED_TAG = "미해결"; // 이 태그 붙이면 처리 완료된 글도 다시 분석
const RESOLVED_TAGS = ["해결", "완료"];
// 봇이 상태 표시용으로 관리하는 태그들 (교체 시 이것만 떼고 나머지 태그는 보존)
const STATUS_TAG_NAMES = ["대기", "수정중", "완료", "해결", "미해결"];

if (!TOKEN || !FORUM_ID || !REPO_ROOT) {
  console.error(".env에 DISCORD_TOKEN / QA_FORUM_CHANNEL_ID / REPO_ROOT 필요. .env.example 참고.");
  process.exit(1);
}

// ---------- 상태 저장 (처리한 글 기록) ----------
const DATA_DIR = path.join(__dirname, "data");
const STATE_FILE = path.join(DATA_DIR, "state.json");
fs.mkdirSync(DATA_DIR, { recursive: true });

let state = {};
try {
  state = JSON.parse(fs.readFileSync(STATE_FILE, "utf8"));
} catch {
  state = {};
}
function saveState() {
  fs.writeFileSync(STATE_FILE, JSON.stringify(state, null, 2));
}

// 작동 스위치: /callclaude 켜기, /stopclaude 끄기 (재시작해도 기억함)
function isActive() {
  return !!state._active;
}
function setActive(v) {
  state._active = v;
  saveState();
}

// ---------- git / gh 실행 ----------
function run(cmd, args, cwd, timeoutMs = 10 * 60 * 1000) {
  return new Promise((resolve, reject) => {
    // shell 사용 금지 — 인자에 공백·한글 있으면 셸 인용 문제로 깨짐 (git/gh는 .exe라 불필요)
    execFile(
      cmd,
      args,
      { cwd, timeout: timeoutMs, maxBuffer: 32 * 1024 * 1024 },
      (err, stdout, stderr) => {
        if (err) reject(new Error(`${cmd} ${args.join(" ")} 실패: ${(stderr || err.message).slice(0, 800)}`));
        else resolve(stdout.trim());
      }
    );
  });
}
const git = (args, cwd = WORKSPACE) => run("git", args, cwd);

// 봇 작업 폴더 준비: origin/main 최신 상태의 깨끗한 워크트리로 만듦
async function ensureWorkspace() {
  await git(["fetch", REMOTE, MAIN_BRANCH], REPO_ROOT);
  if (!fs.existsSync(WORKSPACE)) {
    console.log(`봇 작업 폴더 생성: ${WORKSPACE} (최초 1회, 시간 걸림)`);
    await git(["worktree", "add", "--detach", WORKSPACE, `${REMOTE}/${MAIN_BRANCH}`], REPO_ROOT);
  } else {
    await git(["checkout", "--detach", `${REMOTE}/${MAIN_BRANCH}`]);
    await git(["reset", "--hard"]);
    await git(["clean", "-fd"]);
  }
}

// ---------- Claude 실행 ----------
function runClaude(prompt, { allowedTools, timeoutMs, cwd }) {
  return new Promise((resolve, reject) => {
    const args = ["-p", "--output-format", "text"];
    if (allowedTools) args.push("--allowedTools", allowedTools);

    const child = spawn(CLAUDE_CMD, args, {
      cwd,
      shell: process.platform === "win32",
      windowsHide: true,
    });

    let out = "";
    let err = "";
    const timer = setTimeout(() => {
      child.kill();
      reject(new Error("Claude 실행 시간 초과"));
    }, timeoutMs);

    child.stdout.on("data", (d) => (out += d));
    child.stderr.on("data", (d) => (err += d));
    child.on("error", (e) => {
      clearTimeout(timer);
      reject(e);
    });
    child.on("close", (code) => {
      clearTimeout(timer);
      if (code === 0) resolve(out.trim());
      else reject(new Error(`Claude 종료 코드 ${code}: ${err.slice(0, 500)}`));
    });

    child.stdin.write(prompt);
    child.stdin.end();
  });
}

// ---------- 작업 큐 (동시 실행 방지 — 작업 폴더를 하나만 쓰므로 반드시 순차) ----------
let queue = Promise.resolve();
function enqueue(job) {
  queue = queue.then(job).catch((e) => console.error("작업 실패:", e));
  return queue;
}

// ---------- 디스코드 유틸 ----------
async function sendChunked(thread, text) {
  const chunks = [];
  let rest = text;
  while (rest.length > 0) {
    chunks.push(rest.slice(0, 1900));
    rest = rest.slice(1900);
  }
  let last = null;
  for (const c of chunks) last = await thread.send(c);
  return last;
}

function threadTagNames(thread) {
  const forum = thread.parent;
  if (!forum || !forum.availableTags) return [];
  return (thread.appliedTags || [])
    .map((id) => forum.availableTags.find((t) => t.id === id)?.name)
    .filter(Boolean);
}

async function setTag(thread, key) {
  try {
    const forum = thread.parent;
    if (!forum || !forum.availableTags) return;
    const wanted = Array.isArray(TAG_NAMES[key]) ? TAG_NAMES[key] : [TAG_NAMES[key]];
    let tag = null;
    for (const n of wanted) {
      tag = forum.availableTags.find((t) => t.name === n);
      if (tag) break;
    }
    if (!tag) return;
    // 봇 상태 태그만 교체, 멤버가 붙인 분류 태그는 보존 (포럼 태그 최대 5개)
    const statusIds = forum.availableTags
      .filter((t) => STATUS_TAG_NAMES.includes(t.name))
      .map((t) => t.id);
    const keep = (thread.appliedTags || []).filter((id) => !statusIds.includes(id));
    await thread.setAppliedTags([...keep, tag.id].slice(0, 5));
  } catch (e) {
    console.warn("태그 갱신 실패(무시):", e.message);
  }
}

async function downloadAttachments(message, threadId) {
  const paths = [];
  if (!message || message.attachments.size === 0) return paths;
  const dir = path.join(DATA_DIR, "attachments", threadId);
  fs.mkdirSync(dir, { recursive: true });
  for (const [, att] of message.attachments) {
    try {
      const res = await fetch(att.url);
      const buf = Buffer.from(await res.arrayBuffer());
      const file = path.join(dir, att.name);
      fs.writeFileSync(file, buf);
      paths.push(file);
    } catch (e) {
      console.warn("첨부 다운로드 실패:", att.name, e.message);
    }
  }
  return paths;
}

async function collectIssueText(thread) {
  let starter = null;
  try {
    starter = await thread.fetchStarterMessage();
  } catch {}

  const messages = await thread.messages.fetch({ limit: 30 });
  const chat = [...messages.values()]
    .filter((m) => !m.author.bot)
    .reverse()
    .map((m) => `${m.author.displayName || m.author.username}: ${m.content}`)
    .join("\n");

  const attachmentPaths = starter
    ? await downloadAttachments(starter, thread.id)
    : [];

  return {
    title: thread.name,
    body: starter ? starter.content : "",
    chat,
    attachmentPaths,
  };
}

// ---------- 핵심 흐름 ----------
// opts.force: /askclaude로 호출됨 — 상태·일시정지 무시하고 이 글만 다시 분석
// opts.note: /askclaude에 딸려온 추가 요청 텍스트
async function handleNewIssue(thread, opts = {}) {
  if (threadTagNames(thread).includes(NOTICE_TAG)) return; // 공지 글은 무시
  const s = state[thread.id];
  if (!opts.force) {
    if (!isActive()) return; // 일시정지 중
    if (s && !["error", "planning"].includes(s.status)) return; // 이미 처리 중이거나 완료
  }

  state[thread.id] = { status: "planning", manual: !!opts.force };
  saveState();

  console.log(`[계획] ${thread.name}`);
  await setTag(thread, "waiting");

  const issue = await collectIssueText(thread);
  const attachNote = issue.attachmentPaths.length
    ? `\n첨부 파일(스크린샷 등, Read 도구로 열어볼 것):\n${issue.attachmentPaths.join("\n")}`
    : "";

  const prompt = `너는 유니티 게임 프로젝트의 QA 이슈 분석 담당이다.
아래 QA 이슈를 읽고, 이 레포에서 관련 코드를 찾아 간략한 수정 계획을 작성하라.

규칙:
- 절대 코드를 수정하지 말 것. 계획만 작성.
- 계획은 5줄 이내. 구구절절 금지.
- 형식: "원인 추정" 1~2줄, "수정 방법" 2~3줄, 관련 파일 경로 명시.
- 관련 코드를 못 찾았거나 코드 수정으로 해결할 이슈가 아니면(기획 논의, 에셋 제작 등) 그렇다고 솔직히 말할 것.
- 한국어로 작성.
- 말투는 원시인처럼: 짧고 단순한 문장, 조사·존댓말 생략. 예: "원인 찾음. LobbyPanel 버튼 연결 빠짐. 고치면 됨." "우가"·"크르릉" 같은 의성어는 쓰지 말 것. 파일 경로·클래스명·기술 내용은 정확하게.

=== QA 이슈 ===
제목: ${issue.title}
내용: ${issue.body}
${issue.chat ? `\n스레드 대화:\n${issue.chat}` : ""}${attachNote}${
    opts.note ? `\n\n=== 추가 요청 (이번에 특별히 반영할 것) ===\n${opts.note}` : ""
  }${
    opts.force
      ? `\n\n참고: 이 글은 이전에 처리됐을 수 있음. 스레드 대화의 최신 피드백과 추가 요청을 우선 반영해 계획을 새로 작성하라.`
      : ""
  }`;

  let plan;
  try {
    await ensureWorkspace(); // 항상 origin/main 최신 기준으로 분석
    plan = await runClaude(prompt, {
      allowedTools: "Read,Glob,Grep",
      timeoutMs: PLAN_TIMEOUT_MS,
      cwd: WORKSPACE,
    });
  } catch (e) {
    state[thread.id] = { status: "error", manual: !!opts.force };
    saveState();
    await thread.send(`⚠️ 계획 생성 실패: ${e.message}`);
    return;
  }

  const planMsg = await sendChunked(
    thread,
    `📋 **수정 계획** (기준: ${REMOTE}/${MAIN_BRANCH} 최신)\n\n${plan}\n\n${EMOJI_OK} 누르면 브랜치 만들어 수정 진행 · ${EMOJI_NO} 누르면 건너뜀`
  );
  await planMsg.react(EMOJI_OK);
  await planMsg.react(EMOJI_NO);

  state[thread.id] = {
    status: "awaiting_confirm",
    planMessageId: planMsg.id,
    plan,
    manual: !!opts.force,
  };
  saveState();
}

// ---------- CI 감시 (푸시 후 결과만 스레드에 보고 — 자동 수정은 안 함) ----------
const kCiPollMs    = 60 * 1000;       // 1분 간격 폴링
const kCiTimeoutMs = 30 * 60 * 1000;  // 30분 넘으면 포기(결과는 PR 페이지에 있음)

// 방금 푸시한 커밋(sha)의 CI 런을 찾아 끝날 때까지 기다렸다가 결과를 스레드에 보고.
// 큐 밖에서 돌므로 다음 수정 작업을 막지 않는다. 봇을 재시작하면 감시는 사라진다.
async function watchCi(thread, branch, sha) {
  const deadline = Date.now() + kCiTimeoutMs;
  let ci = null;
  while (Date.now() < deadline) {
    try {
      const out = await run("gh", ["run", "list", "--branch", branch, "--limit", "3",
        "--json", "databaseId,status,conclusion,headSha,url"], WORKSPACE);
      ci = JSON.parse(out || "[]").find((r) => r.headSha === sha) || null;
      if (ci && ci.status === "completed") break;
    } catch (e) {
      console.warn("CI 조회 실패(재시도):", e.message);
    }
    await new Promise((r) => setTimeout(r, kCiPollMs));
  }
  if (!ci || ci.status !== "completed") {
    if (ci) await thread.send(`⏳ CI가 30분 넘게 안 끝남 — 결과는 여기서 확인: ${ci.url}`);
    return;
  }
  if (ci.conclusion === "success") {
    await thread.send("🟢 **CI 통과** — 테스트 전부 초록. 머지해도 됨.");
    return;
  }
  // 실패·취소 등: 실패한 잡 이름까지 붙여서 보고
  let detail = "";
  try {
    const jobs = await run("gh", ["run", "view", String(ci.databaseId), "--json", "jobs",
      "-q", '[.jobs[] | select(.conclusion=="failure") | .name] | join(", ")'], WORKSPACE);
    if (jobs) detail = `\n실패한 잡: ${jobs}`;
  } catch {}
  await thread.send(
    `🔴 **CI ${ci.conclusion === "failure" ? "실패" : ci.conclusion}** — 머지 전에 확인 필요.${detail}\n${ci.url}`
  );
}

async function handleConfirm(thread, approvedBy) {
  const s = state[thread.id];
  if (!s || s.status !== "awaiting_confirm") return;
  if (!isActive() && !s.manual) return; // 일시정지 중 (수동 호출 글은 예외, 안내는 반응 핸들러가 함)

  s.status = "fixing";
  saveState();
  console.log(`[수정] ${thread.name} (승인: ${approvedBy})`);

  await setTag(thread, "fixing");
  await thread.send(`🔧 수정 시작함 (승인: ${approvedBy}). ${MAIN_BRANCH} 최신 기준 새 브랜치에서 작업 후 푸시함.`);

  const branch = `qa/issue-${thread.id}`;
  const threadUrl = `https://discord.com/channels/${thread.guildId}/${thread.id}`;
  const issue = await collectIssueText(thread);

  const prompt = `너는 유니티 게임 프로젝트의 QA 수정 담당이다.
아래 QA 이슈를 아래 수정 계획대로 수정하라.

규칙:
- **스레드 대화에 수정 계획에 대한 피드백·변경 지시가 있으면(예: "~로 해서 진행하죠", "~는 빼고") 그 지시가 계획보다 우선이다.** 계획과 대화가 충돌하면 대화의 최신 지시를 따르고 보고에 명시하라.
- git 명령은 실행하지 말 것. 파일 수정만. (커밋·푸시는 외부에서 처리함)
- 수정 후 무엇을 어떻게 바꿨는지 3~5줄로 요약 보고 (변경 파일 경로 포함).
- 계획대로 하다가 문제 발견하면 합리적으로 조정하되 보고에 명시.
- 한국어로 작성.
- 보고 말투는 원시인처럼: 짧고 단순한 문장, 조사·존댓말 생략. "우가"·"크르릉" 같은 의성어는 쓰지 말 것. 파일 경로·클래스명·코드 내용은 정확하게. 코드 자체는 평범하고 전문적으로 작성.

=== QA 이슈 ===
제목: ${issue.title}
내용: ${issue.body}
${issue.chat ? `\n스레드 대화:\n${issue.chat}` : ""}

=== 수정 계획 ===
${s.plan}`;

  let result;
  let prUrl = null;
  try {
    await ensureWorkspace();
    await git(["checkout", "-B", branch, `${REMOTE}/${MAIN_BRANCH}`]);

    result = await runClaude(prompt, {
      allowedTools: "Read,Glob,Grep,Edit,Write",
      timeoutMs: FIX_TIMEOUT_MS,
      cwd: WORKSPACE,
    });

    await git(["add", "-A"]);
    const diff = await git(["diff", "--cached", "--stat"]);
    if (!diff) {
      s.status = "awaiting_confirm";
      saveState();
      await sendChunked(thread, `🤔 변경된 파일이 없음. Claude 보고:\n\n${result}\n\n다시 시도하려면 ${EMOJI_OK} 다시 눌러줘.`);
      return;
    }

    // 여러 줄 메시지는 인자 인용 문제를 피하려고 파일로 전달
    const commitMsg = `fix(qa): ${issue.title}\n\n${result}\n\nDiscord QA: ${threadUrl}\n승인: ${approvedBy}\n\nCo-Authored-By: Claude Fable 5 <noreply@anthropic.com>`;
    const msgFile = path.join(DATA_DIR, "commit-msg.txt");
    fs.writeFileSync(msgFile, commitMsg, "utf8");
    await git(["commit", "-F", msgFile]);
    var pushedSha = await git(["rev-parse", "HEAD"]);   // CI 감시가 이 커밋의 런을 특정하는 데 씀
    await git(["push", "-f", "-u", REMOTE, branch]);

    if (CREATE_PR) {
      try {
        const prBodyFile = path.join(DATA_DIR, "pr-body.md");
        fs.writeFileSync(
          prBodyFile,
          `디스코드 QA 이슈 자동 수정.\n\n이슈: ${threadUrl}\n승인: ${approvedBy}\n\n${result}\n\n🤖 Generated with [Claude Code](https://claude.com/claude-code)`,
          "utf8"
        );
        prUrl = await run(
          "gh",
          ["pr", "create", "--head", branch, "--base", MAIN_BRANCH,
           "--title", `fix(qa): ${issue.title}`,
           "--body-file", prBodyFile],
          WORKSPACE
        );
      } catch (e) {
        // 이미 열린 PR이 있으면 그 URL 재사용
        try {
          prUrl = await run("gh", ["pr", "view", branch, "--json", "url", "-q", ".url"], WORKSPACE);
        } catch {
          console.warn("PR 생성 실패(브랜치 푸시는 됨):", e.message);
        }
      }
    }

    await git(["checkout", "--detach", `${REMOTE}/${MAIN_BRANCH}`]);
  } catch (e) {
    s.status = "awaiting_confirm";
    saveState();
    await thread.send(`⚠️ 수정 실패: ${e.message}\n다시 시도하려면 계획 메시지에 ${EMOJI_OK} 다시 눌러줘.`);
    return;
  }

  s.status = "done";
  s.branch = branch;
  saveState();
  await setTag(thread, "done");
  await sendChunked(
    thread,
    `✅ **수정 완료 — 브랜치 푸시됨**\n\n${result}\n\n` +
      `🌿 브랜치: \`${branch}\`` +
      (prUrl ? `\n🔀 PR: ${prUrl}` : `\n(PR 자동 생성은 실패 — 브랜치에서 직접 PR 열면 됨)`)
  );
  watchCi(thread, branch, pushedSha).catch((e) => console.warn("CI 감시 실패:", e.message));
}

async function handleSkip(thread, by) {
  const s = state[thread.id];
  if (!s || s.status !== "awaiting_confirm") return;
  s.status = "skipped";
  saveState();
  await thread.send(`⏭️ 건너뜀 (${by}). 나중에 다시 하려면 계획 메시지 반응 지우고 다시 ${EMOJI_OK} 눌러줘.`);
}

// ---------- 클라이언트 ----------
const client = new Client({
  intents: [
    GatewayIntentBits.Guilds,
    GatewayIntentBits.GuildMessages,
    GatewayIntentBits.MessageContent,
    GatewayIntentBits.GuildMessageReactions,
  ],
  partials: [Partials.Message, Partials.Channel, Partials.Reaction],
});

// 기존 글 백로그 스캔 → 미처리 글 큐에 넣음. 넣은 개수 반환.
async function scanBacklog() {
  const forum = await client.channels.fetch(FORUM_ID);
  if (!forum || forum.type !== ChannelType.GuildForum) {
    console.error("QA_FORUM_CHANNEL_ID가 포럼 채널이 아님. ID 확인 필요.");
    return 0;
  }
  const active = await forum.threads.fetchActive();
  const archived = await forum.threads.fetchArchived();
  const all = [...active.threads.values(), ...archived.threads.values()];
  let queued = 0;
  for (const thread of all) {
    const names = threadTagNames(thread);
    if (names.includes(NOTICE_TAG)) continue; // 공지 글 무시
    const s = state[thread.id];
    if (!s && RESOLVED_TAGS.some((t) => names.includes(t))) continue; // 이미 해결 표시된 옛 글
    if (!s || ["error", "planning"].includes(s.status)) {
      enqueue(() => handleNewIssue(thread));
      queued++;
    } else if (names.includes(UNRESOLVED_TAG) && ["done", "skipped"].includes(s.status)) {
      // 미해결 태그 → 처리 끝난 글도 다시 분석
      enqueue(() =>
        handleNewIssue(thread, {
          force: true,
          note: "미해결 태그가 붙어 있음 — 이전 처리 이후의 스레드 피드백을 반영해 계획을 다시 작성하라.",
        })
      );
      queued++;
    }
  }
  console.log(`포럼 글 ${all.length}개 중 미처리 ${queued}개 큐에 넣음.`);
  return queued;
}

client.once(Events.ClientReady, async () => {
  console.log(`봇 로그인: ${client.user.tag} (상태: ${isActive() ? "작동 중" : "일시정지"})`);

  // 슬래시 명령어 등록 (포럼이 속한 서버에 즉시 등록)
  try {
    const forum = await client.channels.fetch(FORUM_ID);
    await forum.guild.commands.set([
      { name: "callclaude", description: "QA봇 작동 시작 — 밀린 글 스캔 + 새 글 자동 처리" },
      { name: "stopclaude", description: "QA봇 일시정지 — 하던 작업 하나는 마저 끝냄" },
      { name: "qastatus", description: "QA봇 현황 보기" },
      {
        name: "askclaude",
        description: "이 QA 글만 클로드 호출 — 계획 다시 작성, 추가 수정 요청 (일시정지 중에도 됨)",
        options: [
          {
            type: 3, // STRING
            name: "note",
            description: "추가로 시킬 내용 (선택)",
            required: false,
          },
        ],
      },
      { name: "tokenusage", description: "토큰 사용량 리포트 — 내 세션 vs QA봇 (오늘·7일·누적)" },
    ]);
    console.log("슬래시 명령어 등록 완료: /callclaude /stopclaude /qastatus /askclaude /tokenusage");
  } catch (e) {
    console.error("명령어 등록 실패:", e.message);
  }

  if (isActive()) scanBacklog().catch((e) => console.error("백로그 스캔 실패:", e));

  // 매일 REPORT_HOUR시 이후 첫 체크 때 어제분 토큰 리포트를 채널에 올림 (하루 1회, 봇 일시정지와 무관)
  if (REPORT_CHANNEL_ID) {
    const tryDailyReport = async () => {
      const now = new Date();
      if (now.getHours() < REPORT_HOUR) return;
      const todayKey = now.toLocaleDateString("sv"); // YYYY-MM-DD (로컬 기준)
      if (state._lastTokenReport === todayKey) return;
      state._lastTokenReport = todayKey; // 실패해도 당일 재시도 폭주 방지 — 다음날 다시 시도
      saveState();
      try {
        const ch = await client.channels.fetch(REPORT_CHANNEL_ID);
        const report = await buildReport({ repoRoot: REPO_ROOT, workspace: WORKSPACE, mode: "yesterday" });
        await ch.send(report.slice(0, 2000));
        console.log("일일 토큰 리포트 전송 완료");
      } catch (e) {
        console.error("일일 토큰 리포트 실패:", e.message);
      }
    };
    tryDailyReport();
    setInterval(tryDailyReport, 5 * 60 * 1000);
  }
});

client.on(Events.InteractionCreate, async (interaction) => {
  if (!interaction.isChatInputCommand()) return;
  try {
    if (interaction.commandName === "callclaude") {
      if (isActive()) {
        await interaction.reply("이미 작동 중임. 🔧");
        return;
      }
      setActive(true);
      await interaction.reply("🟢 QA봇 작동 시작. 밀린 글 스캔함.");
      const n = await scanBacklog();
      await interaction.followUp(`미처리 글 ${n}개 발견, 순차 처리 시작. 새 글도 자동 감지함.`);
    } else if (interaction.commandName === "stopclaude") {
      if (!isActive()) {
        await interaction.reply("이미 일시정지 상태임. 💤");
        return;
      }
      setActive(false);
      await interaction.reply("💤 QA봇 일시정지. 진행 중이던 작업 하나는 마저 끝냄. 다시 시작은 /callclaude");
    } else if (interaction.commandName === "qastatus") {
      const counts = {};
      for (const [k, v] of Object.entries(state)) {
        if (k.startsWith("_")) continue;
        counts[v.status] = (counts[v.status] || 0) + 1;
      }
      const label = {
        planning: "분석 중",
        awaiting_confirm: "컨펌 대기",
        fixing: "수정 중",
        done: "완료",
        skipped: "건너뜀",
        error: "오류",
      };
      const lines = Object.entries(counts)
        .map(([k, v]) => `- ${label[k] || k}: ${v}개`)
        .join("\n");
      await interaction.reply(
        `상태: ${isActive() ? "🟢 작동 중" : "💤 일시정지"}\n${lines || "처리 기록 없음"}`
      );
    } else if (interaction.commandName === "askclaude") {
      const ch = interaction.channel;
      if (!ch || !ch.isThread() || ch.parentId !== FORUM_ID) {
        await interaction.reply({
          content: "QA 포럼 글(스레드) 안에서만 쓸 수 있음.",
          ephemeral: true,
        });
        return;
      }
      const s = state[ch.id];
      if (s && ["planning", "fixing"].includes(s.status)) {
        await interaction.reply("이 글은 지금 작업 중임. 끝나면 다시 불러줘.");
        return;
      }
      const note = interaction.options.getString("note") || "";
      await interaction.reply(
        `🙋 접수. 이 글 다시 분석함${note ? ` — 요청: "${note}"` : ""}. 계획 곧 올림.`
      );
      enqueue(() => handleNewIssue(ch, { force: true, note }));
    } else if (interaction.commandName === "tokenusage") {
      await interaction.deferReply(); // 로그 파싱에 몇 초 걸릴 수 있음
      try {
        const report = await buildReport({ repoRoot: REPO_ROOT, workspace: WORKSPACE, mode: "today" });
        await interaction.editReply(report.slice(0, 2000));
      } catch (e) {
        await interaction.editReply(`⚠️ 리포트 생성 실패: ${e.message}`);
      }
    }
  } catch (e) {
    console.error("명령어 처리 실패:", e);
  }
});

client.on(Events.ThreadCreate, (thread, newlyCreated) => {
  if (!newlyCreated || thread.parentId !== FORUM_ID) return;
  if (!isActive()) return;
  // 포럼 글 생성 직후에는 본문 메시지가 아직 없을 수 있어 잠깐 대기
  setTimeout(() => enqueue(() => handleNewIssue(thread)), 3000);
});

client.on(Events.MessageReactionAdd, async (reaction, user) => {
  try {
    if (user.bot) return;
    if (reaction.partial) await reaction.fetch();
    const msg = reaction.message;
    const thread = msg.channel;
    if (!thread.isThread() || thread.parentId !== FORUM_ID) return;

    const s = state[thread.id];
    if (!s || s.planMessageId !== msg.id) return;

    if (!isActive() && !s.manual) {
      await thread.send("💤 봇 일시정지 중이라 처리 안 됨. /callclaude 로 깨우거나, 이 글만 하려면 /askclaude 쓰면 됨.");
      return;
    }

    const name = user.displayName || user.username;
    if (reaction.emoji.name === EMOJI_OK)
      enqueue(() => handleConfirm(thread, name));
    else if (reaction.emoji.name === EMOJI_NO)
      enqueue(() => handleSkip(thread, name));
  } catch (e) {
    console.error("반응 처리 실패:", e);
  }
});

client.login(TOKEN);
