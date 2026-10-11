#!/usr/bin/env bash
# Runs Unity batch compile / tests for this checkout. Never closes an existing Editor.
#
#   tools/run-tests.sh [--mode Compile|EditMode|PlayMode|All] [--filter NAME]
#                      [--timeout SECONDS] [--unity CLI] [--python EXE] [--dry-run]
#
# All runs Compile then PlayMode. Results go to Logs/UnityTests/<run-id>/.
set -euo pipefail

mode=All
filter=
timeout=600
unity=unity
python=
dry_run=0

while [ $# -gt 0 ]; do
	case "$1" in
		--mode) mode=$2; shift 2 ;;
		--filter) filter=$2; shift 2 ;;
		--timeout) timeout=$2; shift 2 ;;
		--unity) unity=$2; shift 2 ;;
		--python) python=$2; shift 2 ;;
		--dry-run) dry_run=1; shift ;;
		-h|--help) sed -n '2,7p' "$0"; exit 0 ;;
		*) echo "Unknown option: $1" >&2; exit 2 ;;
	esac
done

die() { echo "error: $*" >&2; exit 1; }

root=$(cd "$(dirname "$0")/.." && pwd)
# Unity CLI on Windows expects a Windows path.
project=$( (cd "$root" && pwd -W) 2>/dev/null || echo "$root")

case "$mode" in
	Compile|EditMode|PlayMode) modes=("$mode") ;;
	All) modes=(Compile PlayMode) ;;
	*) die "--mode must be Compile, EditMode, PlayMode or All" ;;
esac
[ -n "$filter" ] && [ "$mode" = Compile ] && die "--filter applies to tests, not Compile"

version=$(sed -n 's/^m_EditorVersion:[[:space:]]*//p' "$root/ProjectSettings/ProjectVersion.txt" | tr -d '\r')
[ -n "$version" ] || die "ProjectVersion.txt has no Editor version"

command -v "$unity" >/dev/null || die "Unity CLI not found: $unity"
if [ -z "$python" ]; then
	if command -v python3 >/dev/null && python3 -c '' 2>/dev/null; then python=python3; else python=python; fi
fi
command -v "$python" >/dev/null || die "Python not found: $python"

if [ "$dry_run" = 1 ]; then
	printf 'project=%s\neditorVersion=%s\nmodes=%s\nfilter=%s\ntimeout=%s\nunity=%s\npython=%s\n' \
		"$project" "$version" "${modes[*]}" "$filter" "$timeout" "$(command -v "$unity")" "$(command -v "$python")"
	exit 0
fi

assert_editor_closed() {
	# An open Editor holds Temp/UnityLockfile exclusively. Only read it; never modify or remove it.
	local lock="$root/Temp/UnityLockfile"
	if [ -e "$lock" ] && ! (exec 3<"$lock") 2>/dev/null; then
		die "UIControl Editor is open. Close it before batch runs, or use 'unity command run_tests' instead."
	fi
}

artifacts="$root/Logs/UnityTests"
mkdir -p "$artifacts"
runner_lock="$artifacts/.run-tests.lock"
mkdir "$runner_lock" 2>/dev/null || die "Another run is in progress ($runner_lock)"
trap 'rmdir "$runner_lock"' EXIT

assert_editor_closed
run_id="$(date +%Y%m%d-%H%M%S)-$(od -An -N4 -tx1 /dev/urandom | tr -d ' \n')"
out="$artifacts/$run_id"
mkdir -p "$out"
common=("$project" --editor-version "$version" --timeout "$timeout" --non-interactive --no-banner)

for m in "${modes[@]}"; do
	assert_editor_closed
	editor_log="$out/$m.editor.log"
	cli_log="$out/$m.cli.log"
	echo "Running $m on Unity $version. Logs: $out"
	set +e
	if [ "$m" = Compile ]; then
		"$unity" run "${common[@]}" -- -executeMethod UIControl.Testing.Editor.CompilationCheck.Run \
			-logFile "$editor_log" >"$cli_log" 2>&1
		cli_exit=$?
		errors=$(grep -ciE 'error CS[0-9]+|Scripts have compiler errors|Compilation failed' "$editor_log" 2>/dev/null)
		grep -q UICONTROL_COMPILATION_OK "$editor_log" 2>/dev/null
		marker=$?
		set -e
		echo "{\"mode\": \"Compile\", \"cliExitCode\": $cli_exit, \"compilerErrorCount\": ${errors:-0}}" | tee "$out/$m.summary.json"
		[ "$cli_exit" = 0 ] && [ "$marker" = 0 ] && [ "${errors:-0}" = 0 ] || die "Compile did not pass. Inspect $cli_log and $editor_log"
	else
		report="$out/$m.xml"
		args=(test "${common[@]}" --mode "$m" --output "$report" --report-format nunit)
		[ -n "$filter" ] && args+=(--filter "$filter")
		"$unity" "${args[@]}" -- -logFile "$editor_log" >"$cli_log" 2>&1
		cli_exit=$?
		"$python" "$root/tools/parse-result.py" "$report" | tee "$out/$m.summary.json"
		parser_exit=${PIPESTATUS[0]}
		set -e
		[ "$cli_exit" = 0 ] && [ "$parser_exit" = 0 ] || die "$m did not pass. Inspect $cli_log and $editor_log"
	fi
done
echo "All stages passed: $out"
