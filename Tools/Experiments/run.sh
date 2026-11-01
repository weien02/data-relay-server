#!/usr/bin/env bash
# Runs the evaluation experiments of report Section 4 (E1 to E4) and keeps every output file.
#
#   ./run.sh                 all experiments, 3 repeats each
#   ./run.sh e3              one experiment (e1e2, e3 or e4)
#   REPS=1 DURATION=5 ./run.sh e1e2    quick smoke test
#
# Each run starts a fresh server (sessions and player ids are never freed), samples the
# server process with ps once per second, runs the harness with --json, then stops the
# server with Ctrl+C. Results land in results/<experiment>/<config>_rep<n>.*, and
# experiments.py summarise turns them into the report's tables.

set -u
cd "$(dirname "$0")"
HERE="$(pwd)"
ROOT="$(cd ../.. && pwd)"

REPS="${REPS:-3}"
DURATION="${DURATION:-60}"
PORT="${PORT:-5000}"
WHICH="${1:-all}"

SERVER_DLL="$ROOT/RelayServer/RelayServer/bin/Release/net8.0/RelayServer.dll"
HARNESS_DLL="$ROOT/Tools/StressTest/bin/Release/net8.0/StressTest.dll"

echo "building Release binaries once, so no build happens inside a measurement"
dotnet build -c Release "$ROOT/RelayServer/RelayServer" >/dev/null || { echo "server build failed"; exit 2; }
dotnet build -c Release "$ROOT/Tools/StressTest" >/dev/null || { echo "harness build failed"; exit 2; }

now_ms() { python3 -c 'import time; print(int(time.time() * 1000))'; }

# Datagrams the OS discarded because a receive buffer was full. If this moves during a run, the
# harness, not the server, lost those messages.
udp_full_drops() { netstat -s -p udp | awk '/dropped due to full socket buffers/ {print $1}'; }

start_server() {
    # A script's background jobs start with SIGINT ignored, so the Ctrl+C in stop_server would
    # never reach the server. Job control gives it its own process group with SIGINT left alone.
    set -m
    dotnet "$SERVER_DLL" "$@" > "$base.server.log" 2>&1 &
    server_pid=$!
    set +m
    # Wait for the server to print its sync mode, which comes just before it starts listening.
    for _ in $(seq 1 50); do
        grep -q "sync mode" "$base.server.log" 2>/dev/null && break
        sleep 0.2
    done
    sleep 1
    python3 "$HERE/experiments.py" sample "$server_pid" "$base.ps.csv" &
    sampler_pid=$!
}

stop_server() {
    kill -INT "$server_pid" 2>/dev/null
    for _ in $(seq 1 25); do
        kill -0 "$server_pid" 2>/dev/null || break
        sleep 0.2
    done
    # Still running after 5 s: stop it anyway so the next run gets a fresh server.
    kill -0 "$server_pid" 2>/dev/null && { echo "  server ignored Ctrl+C, terminating"; kill -TERM "$server_pid"; }
    wait "$server_pid" 2>/dev/null
    wait "$sampler_pid" 2>/dev/null
}

# run_idle experiment name rep rate: the server alone, for its CPU floor with no clients.
run_idle() {
    local experiment="$1" name="$2" rep="$3" rate="$4"
    mkdir -p "$HERE/results/$experiment"
    base="$HERE/results/$experiment/${name}_rep${rep}"
    echo "[$experiment] $name rep $rep"
    start_server "$PORT" --sync-rate "$rate"
    local start; start=$(now_ms)
    sleep "$DURATION"
    echo "{\"rate\": $rate, \"startUnixMs\": $start, \"endUnixMs\": $(now_ms)}" > "$base.idle.json"
    stop_server
}

# run_one experiment name rep clients rate mode publishEvery loss [extra harness args...]
run_one() {
    local experiment="$1" name="$2" rep="$3" clients="$4" rate="$5" mode="$6" publish="$7" loss="$8"
    shift 8
    local dir="$HERE/results/$experiment"
    base="$dir/${name}_rep${rep}"
    mkdir -p "$dir"

    local server_args=("$PORT" --sync-rate "$rate")
    [ "$mode" = "full" ] && server_args+=(--full-sync)
    [ "$loss" != "0" ] && server_args+=(--fault-drop-entity-data "$loss")

    cat > "$base.meta.json" <<EOF
{"experiment": "$experiment", "name": "$name", "rep": $rep, "clients": $clients, "rate": $rate,
 "mode": "$mode", "publishEvery": $publish, "loss": $loss, "harnessArgs": "$*"}
EOF

    echo "[$experiment] $name rep $rep"
    start_server "${server_args[@]}"
    local drops_before; drops_before=$(udp_full_drops)

    dotnet "$HARNESS_DLL" --port "$PORT" --clients "$clients" --duration "$DURATION" \
        --publish-every "$publish" --json "$base.json" "$@" > "$base.harness.log" 2>&1
    local harness_exit=$?
    # 0 pass, 1 failed cross check (expected under loss), 2 setup problem.
    [ "$harness_exit" -eq 2 ] && echo "  harness setup problem, see $base.harness.log"

    echo $(( $(udp_full_drops) - drops_before )) > "$base.osdrops"
    stop_server
}

e1e2() {
    # E1 and E2: the original implementation's claims. 5 clients at the demo's 30 Hz.
    for rep in $(seq 1 "$REPS"); do
        # The server's CPU floor: it spins briefly before each frame to send it on time.
        run_idle e1e2 idle_r30 "$rep" 30
        for mode in dirty full; do
            for publish in 1 10; do
                run_one e1e2 "c5_r30_${mode}_pe${publish}" "$rep" 5 30 "$mode" "$publish" 0
            done
        done
    done
}

e3() {
    # E3: scaling at StinkySteak's 20 Hz, every client publishing every frame.
    for rep in $(seq 1 "$REPS"); do
        run_idle e3 idle_r20 "$rep" 20
        for clients in 10 40 80 160 200; do
            run_one e3 "c${clients}_r20_dirty_pe1" "$rep" "$clients" 20 dirty 1 0
        done
    done
}

e4() {
    # E4: correctness and reconnection, in both sync modes, under two loss models.
    #   entity:  the server drops entity data it sends (--fault-drop-entity-data); everything
    #            else, including every client upload, always arrives.
    #   uniform: the harness drops any datagram, in both directions, for every client (--loss).
    # Plus duplication and reordering, and blackouts below the disconnect threshold.
    for rep in $(seq 1 "$REPS"); do
        for mode in dirty full; do
            run_one e4 "c20_r20_${mode}_loss0" "$rep" 20 20 "$mode" 1 0
            for loss in 1 5 10; do
                run_one e4 "c20_r20_${mode}_entity${loss}" "$rep" 20 20 "$mode" 1 "$loss"
                run_one e4 "c20_r20_${mode}_uniform${loss}" "$rep" 20 20 "$mode" 1 0 --loss "$loss" --impair-seed "$rep"
            done
            run_one e4 "c20_r20_${mode}_dupreorder5" "$rep" 20 20 "$mode" 1 0 \
                --duplicate 5 --reorder 5 --impair-seed "$rep"
            # Blackouts below the server's 3 s disconnect threshold (H4). Seeded per repeat so
            # both sync modes see the same kill times.
            run_one e4 "c20_r20_${mode}_blackout" "$rep" 20 20 "$mode" 1 0 \
                --kill 5 --kill-mode blackout --kill-for-min 0.5 --kill-for-max 2.5 --kill-seed "$rep"
        done
    done
}

sweep() {
    # Configured sync rate against the rate clients actually see, and the server's CPU with no
    # clients, across the range the server accepts.
    for rate in 5 10 20 30 60 120 250 500; do
        DURATION=30 run_idle sweep "idle_r${rate}" 1 "$rate"
        DURATION=15 run_one sweep "c2_r${rate}_dirty_pe1" 1 2 "$rate" dirty 1 0
    done
}

case "$WHICH" in
    e1e2) e1e2 ;;
    e3) e3 ;;
    e4) e4 ;;
    sweep) sweep ;;
    all) e1e2; e3; e4; sweep ;;
    *) echo "usage: $0 [e1e2|e3|e4|sweep|all]"; exit 2 ;;
esac
echo "done; summarise with: python3 $HERE/experiments.py summarise"
