#!/usr/bin/env bash
# Send one command to the running Unity editor (Assets/Editor/EditorBridge.cs) and print the result JSON.
#
#   tools/editor-bridge/ub.sh ping
#   tools/editor-bridge/ub.sh refresh            # import + compile changed files (Unity may be in background)
#   tools/editor-bridge/ub.sh wait               # block until Unity answers and is not compiling/importing
#   tools/editor-bridge/ub.sh menu "Olympus/Setup/..."
#   tools/editor-bridge/ub.sh play | stop
#   tools/editor-bridge/ub.sh open Assets/Scenes/Territory.unity
#   tools/editor-bridge/ub.sh shot popup         # -> Temp/EditorBridge/shots/popup.png (play mode only)
#   tools/editor-bridge/ub.sh call "Olympus.Editor.BridgeActions.SelectSlot 3"
#
# Console log and compile errors: Temp/EditorBridge/console.log
# Env: UB_TIMEOUT (seconds, default 60)
#
# Kept ASCII-only on purpose (see CLAUDE.md: non-ASCII scripts break on Windows code pages).

set -u
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
BRIDGE="$ROOT/Temp/EditorBridge"
TIMEOUT="${UB_TIMEOUT:-60}"

cmd="${1:-}"
arg="${2:-}"
if [ -z "$cmd" ]; then
  sed -n '2,16p' "$0"
  exit 2
fi

json_escape() {
  printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g'
}

send() {
  local c="$1" a="$2" id
  id="$(date +%s%N)-$$-$RANDOM"
  mkdir -p "$BRIDGE/inbox" "$BRIDGE/outbox"
  printf '{"id":"%s","cmd":"%s","arg":"%s"}' "$id" "$(json_escape "$c")" "$(json_escape "$a")" > "$BRIDGE/inbox/$id.tmp"
  mv "$BRIDGE/inbox/$id.tmp" "$BRIDGE/inbox/$id.json"

  local out="$BRIDGE/outbox/$id.json" waited=0
  while [ ! -f "$out" ]; do
    sleep 0.25
    waited=$((waited + 1))
    if [ "$waited" -ge $((TIMEOUT * 4)) ]; then
      rm -f "$BRIDGE/inbox/$id.json"
      echo "{\"ok\":false,\"message\":\"timeout after ${TIMEOUT}s - is Unity open on this project? (first time: click the Unity window once so it compiles the bridge)\"}"
      return 1
    fi
  done
  cat "$out"
  echo
  rm -f "$out"
}

unity_running() {
  # Window title of the editor starts with the project folder name ("Olympus - Territory - ...").
  tasklist //FI "IMAGENAME eq Unity.exe" //V //FO CSV 2>/dev/null | grep -q '"Olympus'
}

if [ "$cmd" = "launch" ]; then
  if unity_running; then echo '{"ok":true,"message":"already running"}'; exit 0; fi
  EXE="${UNITY_EXE:-/c/Program Files/Unity/Hub/Editor/$(sed -n 's/^m_EditorVersion: //p' "$ROOT/ProjectSettings/ProjectVersion.txt" | tr -d '\r')/Editor/Unity.exe}"
  [ -f "$EXE" ] || { echo "{\"ok\":false,\"message\":\"Unity not found: $EXE\"}"; exit 1; }
  "$EXE" -projectPath "$(cygpath -w "$ROOT")" >/dev/null 2>&1 &
  echo '{"ok":true,"message":"launching - follow with: UB_TIMEOUT=300 ub.sh wait"}'
  exit 0
fi

if [ "$cmd" != "wait" ] && ! unity_running; then
  echo '{"ok":false,"message":"Unity is not running on this project - run: ub.sh launch"}'
  exit 1
fi

if [ "$cmd" = "wait" ]; then
  # Answers arrive only after a domain reload finishes, so a reply with both flags false means idle.
  end=$((SECONDS + TIMEOUT))
  while [ $SECONDS -lt $end ]; do
    r="$(UB_TIMEOUT=$((end - SECONDS > 0 ? end - SECONDS : 1)) send ping "")" || { echo "$r"; exit 1; }
    case "$r" in
      *'"isCompiling":false'*'"isUpdating":false'*) echo "$r"; exit 0 ;;
    esac
    sleep 1
  done
  echo '{"ok":false,"message":"still compiling/importing"}'
  exit 1
fi

r="$(send "$cmd" "$arg")"; rc=$?
echo "$r"
[ $rc -ne 0 ] && exit 1
case "$r" in *'"ok":true'*) exit 0 ;; *) exit 1 ;; esac
