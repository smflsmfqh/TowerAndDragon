#!/usr/bin/env bash
# Codex에 측정 결과 검토 한 라운드를 맡긴다.
#
# 사용법: codex_review.sh <review_dir>
#   <review_dir> 안에서 review가 아직 없는 가장 높은 번호의 round-N-request.md를 찾아 Codex에 보낸다.
#   1라운드는 새 세션(검토 지침 + 요청서), 2라운드부터는 thread_id로 같은 세션을 이어 요청서만 보낸다.
#
# 산출물 (<review_dir>/):
#   thread_id               Codex 세션 id (1라운드에 기록)
#   round-N-review.json     판정 (verdict.schema.json 형식)
#   round-N-events.jsonl    Codex 이벤트 원본
#   round-N-codex.log       Codex stderr
#
# 환경 변수: BENCH_REVIEW_MAX_ROUNDS (기본 3), BENCH_REVIEW_EFFORT (기본 high)
set -euo pipefail

SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SKILL_DIR/../../.." && pwd)"
MAX_ROUNDS="${BENCH_REVIEW_MAX_ROUNDS:-3}"
EFFORT="${BENCH_REVIEW_EFFORT:-high}"

if [[ $# -ne 1 ]]; then
  echo "usage: $0 <review_dir>" >&2
  exit 2
fi
REVIEW_DIR="$(cd "$1" && pwd)"

round=0
for f in "$REVIEW_DIR"/round-*-request.md; do
  [[ -e "$f" ]] || continue
  n="$(basename "$f" | sed -E 's/^round-([0-9]+)-request\.md$/\1/')"
  (( n > round )) && round="$n"
done
if (( round == 0 )); then
  echo "error: $REVIEW_DIR 에 round-1-request.md 가 없다" >&2
  exit 2
fi
if (( round > MAX_ROUNDS )); then
  echo "error: 최대 라운드($MAX_ROUNDS) 초과. 사용자에게 넘길 것" >&2
  exit 3
fi

REQ="$REVIEW_DIR/round-$round-request.md"
OUT="$REVIEW_DIR/round-$round-review.json"
EVENTS="$REVIEW_DIR/round-$round-events.jsonl"
LOG="$REVIEW_DIR/round-$round-codex.log"
if [[ -e "$OUT" ]]; then
  echo "error: $OUT 가 이미 있다. 다음 라운드 요청서를 먼저 쓸 것" >&2
  exit 2
fi

common=(--json --output-schema "$SKILL_DIR/verdict.schema.json" -o "$OUT"
        -c "model_reasoning_effort=\"$EFFORT\"")

if (( round == 1 )); then
  {
    cat "$SKILL_DIR/review_prompt.md"
    printf '\n\n---\n\n# 라운드 1 요청서\n\n'
    cat "$REQ"
  } | codex exec -C "$REPO_ROOT" -s read-only "${common[@]}" - >"$EVENTS" 2>"$LOG"
  thread_id="$(python3 -c '
import json, sys
for line in open(sys.argv[1], encoding="utf-8"):
    try:
        e = json.loads(line)
    except ValueError:
        continue
    if e.get("type") == "thread.started":
        print(e["thread_id"]); break
' "$EVENTS")"
  if [[ -z "$thread_id" ]]; then
    echo "error: thread_id를 찾지 못했다 ($EVENTS)" >&2
    exit 4
  fi
  echo "$thread_id" >"$REVIEW_DIR/thread_id"
else
  if [[ ! -s "$REVIEW_DIR/thread_id" ]]; then
    echo "error: $REVIEW_DIR/thread_id 가 없다 (1라운드부터 다시)" >&2
    exit 2
  fi
  # resume는 -s/-C를 받지 않으므로 샌드박스는 config로 다시 고정하고 cwd를 저장소 루트로 맞춘다
  {
    printf '# 라운드 %s 요청서\n\n' "$round"
    cat "$REQ"
  } | (cd "$REPO_ROOT" && codex exec resume "$(cat "$REVIEW_DIR/thread_id")" \
        -c 'sandbox_mode="read-only"' "${common[@]}" -) >"$EVENTS" 2>"$LOG"
fi

python3 - "$OUT" <<'PY'
import json, sys
d = json.load(open(sys.argv[1], encoding="utf-8"))
print(f"verdict={d['verdict']} new={len(d['findings'])} "
      f"unresolved={sum(p['status'] == 'unresolved' for p in d['prior_findings'])}")
PY
echo "review: $OUT"
