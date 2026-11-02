#!/usr/bin/env bash
# Real E1000E link smoke: does not claim DHCP, TCP, internet or SMP proof.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ISO="${1:?usage: smoke-network-n1-3095-qemu.sh ISO SERIAL-LOG}"
LOG="${2:?usage: smoke-network-n1-3095-qemu.sh ISO SERIAL-LOG}"
TIMEOUT="${NET3095_N1_TIMEOUT:-150}"
PCAP="${NET3095_N1_PCAP:-$LOG.pcap}"
MODE="${3:-n1}"
IRQ_TRACE="${NET3095_IRQ_TRACE:-0}"
LINK_CYCLE="${NET3095_LINK_CYCLE:-0}"
LINK_CYCLES="${NET3095_LINK_CYCLES:-1}"
[[ "$MODE" == n1 || "$MODE" == n2 || "$MODE" == n3 || "$MODE" == n4 || "$MODE" == n5 || "$MODE" == n6 ]] || {
  echo "[NET3095-N1][FAIL] Expected n1, n2, n3, n4, n5 or n6 mode" >&2; exit 2;
}
[[ "$TIMEOUT" =~ ^[1-9][0-9]*$ ]] && (( TIMEOUT <= 600 )) || {
  echo "[NET3095-N1][FAIL] Invalid timeout" >&2; exit 1;
}
[[ "$IRQ_TRACE" == 0 || "$IRQ_TRACE" == 1 ]] || {
  echo "[NET3095-N1][FAIL] NET3095_IRQ_TRACE must be 0 or 1" >&2; exit 2;
}
[[ "$IRQ_TRACE" == 0 || "$MODE" == n6 ]] || {
  echo "[NET3095-N1][FAIL] IRQ trace diagnostics require isolated n6 mode" >&2; exit 2;
}
[[ "$LINK_CYCLE" == 0 || "$LINK_CYCLE" == 1 ]] || {
  echo "[NET3095-N6][FAIL] NET3095_LINK_CYCLE must be 0 or 1" >&2; exit 2;
}
[[ "$LINK_CYCLE" == 0 || "$MODE" == n6 ]] || {
  echo "[NET3095-N6][FAIL] Link-cycle QMP requires isolated n6 mode" >&2; exit 2;
}
[[ "$LINK_CYCLES" == 1 || "$LINK_CYCLES" == 2 ]] || {
  echo "[NET3095-N6][FAIL] NET3095_LINK_CYCLES must be 1 or 2" >&2; exit 2;
}
[[ "$LINK_CYCLES" == 1 || "$LINK_CYCLE" == 1 ]] || {
  echo "[NET3095-N6][FAIL] Two link cycles require NET3095_LINK_CYCLE=1" >&2; exit 2;
}
[[ -s "$ISO" ]] || { echo "[NET3095-N1][FAIL] Missing ISO" >&2; exit 1; }
command -v qemu-system-x86_64 >/dev/null || exit 1
# Fail closed: evidence files are unique per boot and must never
# truncate an existing host file, including a dangling symlink.
# Refuse aliasing between serial, stderr, host evidence and the PCAP.
declare -A evidence_paths=()
qmp_args=()
qmp_evidence=()
if [[ "$LINK_CYCLE" == 1 ]]; then
  qmp_args=(-qmp "unix:$LOG.qmp,server=on,wait=off")
  qmp_evidence=("$LOG.qmp")
fi
for evidence in "$LOG" "$LOG.stderr" "$LOG.tcp-host" "$LOG.http-host" "$PCAP" "${qmp_evidence[@]}"; do
  [[ ! -e "$evidence" && ! -L "$evidence" ]] || {
    echo "[NET3095-N1][FAIL] Refusing to overwrite existing evidence: $evidence" >&2; exit 1;
  }
  absolute="$(realpath -m -- "$evidence")"
  [[ -z "${evidence_paths[$absolute]:-}" ]] || {
    echo "[NET3095-N1][FAIL] Evidence paths alias: $evidence" >&2; exit 1;
  }
  evidence_paths[$absolute]=1
done
mkdir -p "$(dirname "$LOG")" "$(dirname "$PCAP")"
# noclobber closes the common TOCTOU window between the guard and creation.
( set -C; : > "$LOG" )
trace_args=()
if [[ "$IRQ_TRACE" == 1 ]]; then
  # QEMU trace events are device-side evidence, NOT proof of guest IRQ handling.
  # Require all requested events to exist rather than silently disabling tracing.
  trace_available="$(qemu-system-x86_64 -trace help 2>&1)" || {
    echo "[NET3095-E1000E-IRQ][FAIL] QEMU cannot list trace events" >&2; exit 1;
  }
  for event in e1000e_irq_legacy_notify e1000e_irq_msi_notify e1000e_irq_msix_notify_vec; do
    grep -Eq "^[[:space:]]*${event}([[:space:]]|$)" <<< "$trace_available" || {
      echo "[NET3095-E1000E-IRQ][FAIL] QEMU lacks trace event: $event" >&2; exit 1;
    }
    trace_args+=(-trace "$event")
  done
fi
# N4 uses a CI-owned, loopback-only TCP server. The QEMU guest reaches
# 127.0.0.1 on the host through the SLIRP 10.0.2.2 gateway alias.
server_pid=""
http_pid=""
pid=""
cleanup() {
  if [[ -n "$pid" ]]; then
    kill "$pid" 2>/dev/null || true
    wait "$pid" 2>/dev/null || true
  fi
  if [[ -n "$server_pid" ]]; then
    kill "$server_pid" 2>/dev/null || true
    wait "$server_pid" 2>/dev/null || true
  fi
  if [[ -n "$http_pid" ]]; then
    kill "$http_pid" 2>/dev/null || true
    wait "$http_pid" 2>/dev/null || true
  fi
}
trap cleanup EXIT INT TERM
if [[ "$MODE" == n4 || "$MODE" == n5 || "$MODE" == n6 ]]; then
  [[ ! -e "$LOG.tcp-host" ]] || {
    echo "[NET3095-N4][FAIL] Refusing to overwrite host server evidence" >&2; exit 1;
  }
  python3 -u "$ROOT/tests/network_3095_tcp_host.py" --port 18095 > "$LOG.tcp-host" 2>&1 &
  server_pid=$!
  ready=0
  for _ in {1..50}; do
    if grep -Fq '[NET3095-N4-HOST] READY port=18095' "$LOG.tcp-host"; then
      ready=1
      break
    fi
    if ! kill -0 "$server_pid" 2>/dev/null; then
      cat "$LOG.tcp-host" >&2
      exit 1
    fi
    sleep 0.1
  done
  [[ "$ready" == 1 ]] || {
    cat "$LOG.tcp-host" >&2
    echo "[NET3095-N4][FAIL] Host TCP server did not become ready" >&2
    kill "$server_pid" 2>/dev/null || true
    wait "$server_pid" 2>/dev/null || true
    exit 1
  }
fi
if [[ "$MODE" == n6 ]]; then
  [[ ! -e "$LOG.http-host" ]] || {
    echo "[NET3095-N6][FAIL] Refusing to overwrite HTTP host evidence" >&2; exit 1;
  }
  python3 -u "$ROOT/tests/network_3095_http_host.py" --port 18096 > "$LOG.http-host" 2>&1 &
  http_pid=$!
  ready=0
  for _ in {1..50}; do
    if grep -Fq '[NET3095-N6-HOST] READY port=18096' "$LOG.http-host"; then
      ready=1
      break
    fi
    if ! kill -0 "$http_pid" 2>/dev/null; then
      cat "$LOG.http-host" >&2
      exit 1
    fi
    sleep 0.1
  done
  [[ "$ready" == 1 ]] || {
    cat "$LOG.http-host" >&2
    echo "[NET3095-N6][FAIL] HTTP listener did not become ready" >&2
    exit 1
  }
fi
qemu-system-x86_64 \
  "${trace_args[@]}" "${qmp_args[@]}" \
  -machine q35 -cpu Nehalem -smp 1 -m 2048M \
  -boot d -cdrom "$ISO" \
  -nic none -netdev user,id=net0 -device e1000e,netdev=net0 \
  -object "filter-dump,id=netdump,netdev=net0,file=$PCAP" \
  -vga std -display none -monitor none \
  -serial "file:$LOG" -no-reboot -no-shutdown \
  2> "$LOG.stderr" &
pid=$!
qemu_alive() {
  kill -0 "$pid" 2>/dev/null || return 1
  local state
  state="$(ps -o stat= -p "$pid" 2>/dev/null)" || return 1
  [[ -n "$state" && "$state" != Z* ]]
}

started=$SECONDS
while (( SECONDS - started < TIMEOUT )); do
  if ! qemu_alive; then
    cat "$LOG.stderr" >&2 || true
    echo "[NET3095-N1][FAIL] Guest process exited before valid NIC proof" >&2
    exit 1
  fi
  if grep -Eqi 'kernel panic|fatal: exception|page fault|\[NET3095-N1\]\[FAIL\]' "$LOG"; then
    tail -n 90 "$LOG" >&2
    echo "[NET3095-N1][FAIL] Kernel or NIC failure before proof" >&2
    exit 1
  fi
  if grep -Fq '[NET3095-N1] DHCP=NOT_ATTEMPTED IP=UNASSIGNED' "$LOG"; then
    # TX may leave the descriptor ring after the guest has logged QUEUED.
    # Wait for actual QEMU PCAP evidence, never count QUEUED as transmitted.
    capture_start=$SECONDS
    until python3 "$ROOT/tests/verify_3095_network_n1.py" "$LOG" "$PCAP" >/dev/null 2>&1; do
      qemu_alive || {
        echo "[NET3095-N1][FAIL] Guest exited before TX capture" >&2; exit 1;
      }
      if (( SECONDS - capture_start >= 12 )); then
        python3 "$ROOT/tests/verify_3095_network_n1.py" "$LOG" "$PCAP"
        echo "[NET3095-N1][FAIL] TX capture missing or malformed" >&2
        exit 1
      fi
      sleep 1
    done
    python3 "$ROOT/tests/verify_3095_network_n1.py" "$LOG" "$PCAP"
    if [[ "$MODE" == n2 || "$MODE" == n3 || "$MODE" == n4 || "$MODE" == n5 || "$MODE" == n6 ]]; then
      # N2 requires genuine inbound DHCP OFFER/ACK and a guest-applied lease.
      dhcp_start=$SECONDS
      until python3 "$ROOT/tests/verify_3095_network_n2.py" "$LOG" "$PCAP" >/dev/null 2>&1; do
        qemu_alive || {
          echo "[NET3095-N2][FAIL] Guest exited before DHCP RX proof" >&2; exit 1;
        }
        if grep -Fq '[NET3095-N2][FAIL]' "$LOG"; then
          tail -n 90 "$LOG" >&2
          echo "[NET3095-N2][FAIL] Guest reported DHCP failure" >&2
          exit 1
        fi
        if (( SECONDS - dhcp_start >= 20 )); then
          python3 "$ROOT/tests/verify_3095_network_n2.py" "$LOG" "$PCAP"
          echo "[NET3095-N2][FAIL] No correlated DHCP RX/TX capture" >&2
          exit 1
        fi
        sleep 1
      done
      python3 "$ROOT/tests/verify_3095_network_n2.py" "$LOG" "$PCAP"
    fi
    if [[ "$MODE" == n3 || "$MODE" == n4 || "$MODE" == n5 || "$MODE" == n6 ]]; then
      icmp_start=$SECONDS
      until python3 "$ROOT/tests/verify_3095_network_n3.py" "$LOG" "$PCAP" >/dev/null 2>&1; do
        qemu_alive || {
          echo "[NET3095-N3][FAIL] Guest exited before ICMP RX proof" >&2; exit 1;
        }
        if grep -Fq '[NET3095-N3][FAIL]' "$LOG"; then
          tail -n 90 "$LOG" >&2
          echo "[NET3095-N3][FAIL] Guest reported ICMP failure" >&2
          exit 1
        fi
        if (( SECONDS - icmp_start >= 15 )); then
          python3 "$ROOT/tests/verify_3095_network_n3.py" "$LOG" "$PCAP"
          echo "[NET3095-N3][FAIL] No correlated ICMP echo RX/TX capture" >&2
          exit 1
        fi
        sleep 1
      done
      python3 "$ROOT/tests/verify_3095_network_n3.py" "$LOG" "$PCAP"
    fi
    if [[ "$MODE" == n4 || "$MODE" == n5 || "$MODE" == n6 ]]; then
      tcp_start=$SECONDS
      until python3 "$ROOT/tests/verify_3095_network_n4.py" "$LOG" "$PCAP" >/dev/null 2>&1; do
        qemu_alive || {
          echo "[NET3095-N4][FAIL] Guest exited before TCP proof" >&2; exit 1;
        }
        if grep -Fq '[NET3095-N4][FAIL]' "$LOG"; then
          tail -n 90 "$LOG" >&2
          echo "[NET3095-N4][FAIL] Guest reported TCP failure" >&2
          exit 1
        fi
        if (( SECONDS - tcp_start >= 20 )); then
          python3 "$ROOT/tests/verify_3095_network_n4.py" "$LOG" "$PCAP"
          cat "$LOG.tcp-host" >&2
          echo "[NET3095-N4][FAIL] No correlated TCP RX/TX capture" >&2
          exit 1
        fi
        sleep 1
      done
      python3 "$ROOT/tests/verify_3095_network_n4.py" "$LOG" "$PCAP"
      grep -Fq '[NET3095-N4-HOST] PASS request/reply' "$LOG.tcp-host" || {
        cat "$LOG.tcp-host" >&2
        echo "[NET3095-N4][FAIL] Host did not complete TCP echo" >&2
        exit 1
      }
    fi
    if [[ "$MODE" == n5 || "$MODE" == n6 ]]; then
      dns_start=$SECONDS
      until python3 "$ROOT/tests/verify_3095_network_n5.py" "$LOG" "$PCAP" >/dev/null 2>&1; do
        qemu_alive || {
          echo "[NET3095-N5][FAIL] Guest exited before DNS proof" >&2; exit 1;
        }
        if grep -Fq '[NET3095-N5][FAIL]' "$LOG"; then
          tail -n 90 "$LOG" >&2
          echo "[NET3095-N5][FAIL] Guest reported DNS failure" >&2
          exit 1
        fi
        if (( SECONDS - dns_start >= 15 )); then
          python3 "$ROOT/tests/verify_3095_network_n5.py" "$LOG" "$PCAP"
          echo "[NET3095-N5][FAIL] No correlated DNS query/answer capture" >&2
          exit 1
        fi
        sleep 1
      done
      python3 "$ROOT/tests/verify_3095_network_n5.py" "$LOG" "$PCAP"
    fi
    if [[ "$MODE" == n6 ]]; then
      http_start=$SECONDS
      until python3 "$ROOT/tests/verify_3095_network_n6.py" "$LOG" "$PCAP" >/dev/null 2>&1; do
        qemu_alive || {
          echo "[NET3095-N6][FAIL] Guest exited before HTTP proof" >&2; exit 1;
        }
        if grep -Fq '[NET3095-N6][FAIL]' "$LOG"; then
          tail -n 90 "$LOG" >&2
          echo "[NET3095-N6][FAIL] Guest reported HTTP failure" >&2
          exit 1
        fi
        if (( SECONDS - http_start >= 20 )); then
          python3 "$ROOT/tests/verify_3095_network_n6.py" "$LOG" "$PCAP"
          cat "$LOG.http-host" >&2
          echo "[NET3095-N6][FAIL] No correlated HTTP TCP capture" >&2
          exit 1
        fi
        sleep 1
      done
      python3 "$ROOT/tests/verify_3095_network_n6.py" "$LOG" "$PCAP"
      if ! grep -Fq '[NET3095-N6-HOST] PASS HTTP/1.0 GET /health 200' "$LOG.http-host" ||
         ! grep -Fq '[NET3095-N6-HOST] REPEAT_PASS HTTP/1.0 GET /health 200' "$LOG.http-host"; then
        cat "$LOG.http-host" >&2
        echo "[NET3095-N6][FAIL] Host did not complete both HTTP exchanges" >&2
        exit 1
      fi
      # Wait for the separate 128-frame raw TX proof before starting the
      # post-proof heartbeat window. Never mistake queued frames for PCAP TX.
      burst_start=$SECONDS
      until python3 "$ROOT/tests/verify_3095_network_n6_burst.py" "$LOG" "$PCAP" >/dev/null 2>&1; do
        qemu_alive || {
          echo "[NET3095-N6][FAIL] Guest exited before raw TX burst proof" >&2; exit 1;
        }
        if grep -Fq '[NET3095-N6][FAIL]' "$LOG"; then
          tail -n 90 "$LOG" >&2
          echo "[NET3095-N6][FAIL] Guest rejected raw TX burst" >&2
          exit 1
        fi
        if (( SECONDS - burst_start >= 15 )); then
          python3 "$ROOT/tests/verify_3095_network_n6_burst.py" "$LOG" "$PCAP"
          echo "[NET3095-N6][FAIL] No 128-frame ordered raw TX PCAP proof" >&2
          exit 1
        fi
        sleep 1
      done
      python3 "$ROOT/tests/verify_3095_network_n6_burst.py" "$LOG" "$PCAP"
      # The final HTTP transaction must occur after the last post-burst
      # ICMP reply, not before or during the TX descriptor stress.
      post_http_start=$SECONDS
      until python3 "$ROOT/tests/verify_3095_network_n6_post_http.py" "$LOG" "$PCAP" >/dev/null 2>&1; do
        qemu_alive || {
          echo "[NET3095-N6][FAIL] Guest exited before post-burst HTTP RX proof" >&2; exit 1;
        }
        if grep -Fq '[NET3095-N6][FAIL]' "$LOG"; then
          tail -n 90 "$LOG" >&2
          echo "[NET3095-N6][FAIL] Guest rejected post-burst HTTP RX" >&2
          exit 1
        fi
        if (( SECONDS - post_http_start >= 20 )); then
          python3 "$ROOT/tests/verify_3095_network_n6_post_http.py" "$LOG" "$PCAP"
          cat "$LOG.http-host" >&2
          echo "[NET3095-N6][FAIL] No correlated post-burst HTTP RX/TX capture" >&2
          exit 1
        fi
        sleep 1
      done
      python3 "$ROOT/tests/verify_3095_network_n6_post_http.py" "$LOG" "$PCAP"
      # A seventh ICMP exchange must be observed on the wire AFTER the
      # sixth HTTP FIN/ACK. This catches RX failure after repeated socket
      # teardown, rather than trusting the guest's success marker.
      final_icmp_start=$SECONDS
      until python3 "$ROOT/tests/verify_3095_network_n6_final_icmp.py" "$LOG" "$PCAP" >/dev/null 2>&1; do
        qemu_alive || {
          echo "[NET3095-N6][FAIL] Guest exited before final ICMP RX proof" >&2; exit 1;
        }
        if grep -Fq '[NET3095-N6][FAIL]' "$LOG"; then
          tail -n 90 "$LOG" >&2
          echo "[NET3095-N6][FAIL] Guest rejected final ICMP RX" >&2
          exit 1
        fi
        if (( SECONDS - final_icmp_start >= 15 )); then
          python3 "$ROOT/tests/verify_3095_network_n6_final_icmp.py" "$LOG" "$PCAP"
          echo "[NET3095-N6][FAIL] No final ICMP RX/TX after six HTTP teardowns" >&2
          exit 1
        fi
        sleep 1
      done
      python3 "$ROOT/tests/verify_3095_network_n6_final_icmp.py" "$LOG" "$PCAP"
      if ! grep -Fq '[NET3095-N6-HOST] POST_BURST_PASS HTTP/1.0 GET /health 200' "$LOG.http-host"; then
        cat "$LOG.http-host" >&2
        echo "[NET3095-N6][FAIL] Host did not complete post-burst HTTP" >&2
        exit 1
      fi
      for attempt in 4 5 6; do
        if ! grep -Fq "[NET3095-N6-HOST] POST_BURST_REPEAT_PASS attempt=$attempt HTTP/1.0 GET /health 200" "$LOG.http-host"; then
          cat "$LOG.http-host" >&2
          echo "[NET3095-N6][FAIL] Host did not complete post-burst HTTP attempt=$attempt" >&2
          exit 1
        fi
      done
      if [[ "$LINK_CYCLE" == 1 ]]; then
        # QMP command acceptance is checked separately from the guest's
        # published link state and the new post-up ICMP packets on the wire.
        armed_start=$SECONDS
        until grep -Fq '[NET3095-N6] LINK_CYCLE_ARMED=YES' "$LOG"; do
          qemu_alive || { echo "[NET3095-N6-LINK][FAIL] Guest exited before arm" >&2; exit 1; }
          if (( SECONDS - armed_start >= 12 )); then
            echo "[NET3095-N6-LINK][FAIL] Guest never armed link-cycle probe" >&2; exit 1
          fi
          sleep 1
        done
        python3 "$ROOT/tools/network_3095_qmp_link.py" --socket "$LOG.qmp" --state down
        down_start=$SECONDS
        until grep -Fq '[NET3095-N6] LINK_DOWN_OBSERVED=YES' "$LOG"; do
          qemu_alive || { echo "[NET3095-N6-LINK][FAIL] Guest exited during link-down" >&2; exit 1; }
          if grep -Fq '[NET3095-N6][FAIL]' "$LOG"; then
            tail -n 90 "$LOG" >&2; exit 1
          fi
          if (( SECONDS - down_start >= 15 )); then
            echo "[NET3095-N6-LINK][FAIL] Guest did not observe QMP link-down" >&2; exit 1
          fi
          sleep 1
        done
        python3 "$ROOT/tools/network_3095_qmp_link.py" --socket "$LOG.qmp" --state up
        recovery_start=$SECONDS
        until python3 "$ROOT/tests/verify_3095_network_n6_link_recovery.py" "$LOG" "$PCAP" >/dev/null 2>&1; do
          qemu_alive || { echo "[NET3095-N6-LINK][FAIL] Guest exited during recovery" >&2; exit 1; }
          if grep -Fq '[NET3095-N6][FAIL]' "$LOG"; then
            tail -n 90 "$LOG" >&2; exit 1
          fi
          if (( SECONDS - recovery_start >= 20 )); then
            python3 "$ROOT/tests/verify_3095_network_n6_link_recovery.py" "$LOG" "$PCAP"
            echo "[NET3095-N6-LINK][FAIL] No verified post-up ICMP RX/TX" >&2; exit 1
          fi
          sleep 1
        done
        python3 "$ROOT/tests/verify_3095_network_n6_link_recovery.py" "$LOG" "$PCAP"
        if [[ "$LINK_CYCLES" == 2 ]]; then
          # Second QMP flap must be followed by a NEW ICMP pair on the wire.
          arm2_start=$SECONDS
          until grep -Fq '[NET3095-N6] LINK2_ARMED=YES' "$LOG"; do
            qemu_alive || { echo "[NET3095-N6-LINK][FAIL] Guest exited before second arm" >&2; exit 1; }
            if grep -Fq '[NET3095-N6][FAIL]' "$LOG"; then
              tail -n 90 "$LOG" >&2; exit 1
            fi
            if (( SECONDS - arm2_start >= 12 )); then
              echo "[NET3095-N6-LINK][FAIL] Guest did not re-arm for second cycle" >&2; exit 1
            fi
            sleep 1
          done
          python3 "$ROOT/tools/network_3095_qmp_link.py" --socket "$LOG.qmp" --state down
          down2_start=$SECONDS
          until grep -Fq '[NET3095-N6] LINK2_DOWN_OBSERVED=YES' "$LOG"; do
            qemu_alive || { echo "[NET3095-N6-LINK][FAIL] Guest exited during second link-down" >&2; exit 1; }
            if grep -Fq '[NET3095-N6][FAIL]' "$LOG"; then
              tail -n 90 "$LOG" >&2; exit 1
            fi
            if (( SECONDS - down2_start >= 15 )); then
              echo "[NET3095-N6-LINK][FAIL] Guest did not observe second link-down" >&2; exit 1
            fi
            sleep 1
          done
          python3 "$ROOT/tools/network_3095_qmp_link.py" --socket "$LOG.qmp" --state up
          recovery2_start=$SECONDS
          until python3 "$ROOT/tests/verify_3095_network_n6_link_recovery.py" "$LOG" "$PCAP" --cycles 2 >/dev/null 2>&1; do
            qemu_alive || { echo "[NET3095-N6-LINK][FAIL] Guest exited during second recovery" >&2; exit 1; }
            if grep -Fq '[NET3095-N6][FAIL]' "$LOG"; then
              tail -n 90 "$LOG" >&2; exit 1
            fi
            if (( SECONDS - recovery2_start >= 20 )); then
              python3 "$ROOT/tests/verify_3095_network_n6_link_recovery.py" "$LOG" "$PCAP" --cycles 2
              echo "[NET3095-N6-LINK][FAIL] No second verified post-up ICMP RX/TX" >&2; exit 1
            fi
            sleep 1
          done
          python3 "$ROOT/tests/verify_3095_network_n6_link_recovery.py" "$LOG" "$PCAP" --cycles 2
        fi
      fi
    fi
    # A running QEMU PID is not proof of a live guest (-no-shutdown).
    # Require ongoing managed kernel execution for the stability window.
    heartbeat_before="$(python3 "$ROOT/tests/verify_3095_network_heartbeat.py" "$LOG" --count)"
    sleep 8
    qemu_alive || {
      echo "[NET3095-N1][FAIL] Guest exited after TX proof" >&2; exit 1;
    }
    if grep -Eqi 'kernel panic|fatal: exception|page fault|\[NET3095-N[1-6]\]\[FAIL\]' "$LOG"; then
      tail -n 90 "$LOG" >&2
      echo "[NET3095-N1][FAIL] Guest fault after network proof" >&2
      exit 1
    fi
    # Count alone can be fooled by duplicate or reset markers. Require the
    # entire guest heartbeat sequence to be strictly 1,2,3,... and at least
    # six NEW ticks after the captured pre-wait baseline.
    python3 "$ROOT/tests/verify_3095_network_heartbeat.py" "$LOG" --after-count "$heartbeat_before"
    python3 "$ROOT/tests/verify_3095_network_n1.py" "$LOG" "$PCAP"
    if [[ "$MODE" == n6 ]]; then
      python3 "$ROOT/tests/verify_3095_network_n6.py" "$LOG" "$PCAP"
      python3 "$ROOT/tests/verify_3095_network_n6_burst.py" "$LOG" "$PCAP"
      python3 "$ROOT/tests/verify_3095_network_n6_post_http.py" "$LOG" "$PCAP"
      python3 "$ROOT/tests/verify_3095_network_n6_final_icmp.py" "$LOG" "$PCAP"
      # A complete PCAP is not enough: also require the guest to read back
      # the bound stock E1000E DriverKit node after all TX/RX stress. This
      # checks binding, published interface and handler faults, NOT ISR count.
      python3 "$ROOT/tests/verify_3095_e1000e_irq_binding.py" "$LOG"
      if [[ "$IRQ_TRACE" == 1 ]]; then
        # Correlate the guest's assigned legacy line with a real QEMU
        # legacy assertion. An MSI-only trace must not satisfy this check.
        # Polling remains enabled, so guest ISR execution is NOT proven.
        python3 "$ROOT/tests/verify_3095_e1000e_irq_binding.py" \
          "$LOG" --trace-stderr "$LOG.stderr"
        # This isolated QEMU run must show at least one device-side IRQ
        # notification, not merely pending interrupt bits or successful
        # polling-based packet delivery. Guest ISR execution is STILL unproven.
        python3 "$ROOT/tests/verify_3095_e1000e_irq_trace.py" \
          "$LOG.stderr" --require-notify
      fi
      if [[ "$LINK_CYCLE" == 1 ]]; then
        python3 "$ROOT/tests/verify_3095_network_n6_link_recovery.py" "$LOG" "$PCAP" --cycles "$LINK_CYCLES"
        echo "[NET3095-N6-LINK-QEMU] PASS QMP link down/up cycles=$LINK_CYCLES, guest observation and distinct PCAP ICMP after each recovery; no ISR proof"
      fi
      echo "[NET3095-N6-QEMU] PASS six E1000E HTTP sessions (four after 128 TX + 4 ICMP) + final ICMP after FIN/ACK + DNS/DHCP/ICMP + 8s stability; no HTTPS or production network"
    elif [[ "$MODE" == n5 ]]; then
      python3 "$ROOT/tests/verify_3095_network_n5.py" "$LOG" "$PCAP"
      echo "[NET3095-N5-QEMU] PASS real E1000E DNS query/answer + TCP/DHCP/ICMP + 8s stability; production networking disabled"
    elif [[ "$MODE" == n4 ]]; then
      python3 "$ROOT/tests/verify_3095_network_n4.py" "$LOG" "$PCAP"
      echo "[NET3095-N4-QEMU] PASS real E1000E TCP echo RX/TX + DHCP/ICMP + 8s stability; production networking disabled"
    elif [[ "$MODE" == n3 ]]; then
      python3 "$ROOT/tests/verify_3095_network_n3.py" "$LOG" "$PCAP"
      echo "[NET3095-N3-QEMU] PASS real e1000e DHCP RX + ICMP echo RX/TX + 8s stability; production networking disabled"
    elif [[ "$MODE" == n2 ]]; then
      python3 "$ROOT/tests/verify_3095_network_n2.py" "$LOG" "$PCAP"
      echo "[NET3095-N2-QEMU] PASS real e1000e RX via DHCP DORA + QEMU PCAP + 8s stability; production networking disabled"
    else
      echo "[NET3095-N1-QEMU] PASS real e1000e link + QEMU PCAP raw TX + 8s stability; RX/DHCP unproven"
    fi
    exit 0
  fi
  sleep 1
done
tail -n 100 "$LOG" >&2
echo "[NET3095-N1][FAIL] QEMU did not produce verified NIC/link proof in $TIMEOUT seconds" >&2
exit 1
