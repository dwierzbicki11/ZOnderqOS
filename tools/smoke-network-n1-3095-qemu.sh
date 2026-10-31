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
[[ -s "$ISO" ]] || { echo "[NET3095-N1][FAIL] Missing ISO" >&2; exit 1; }
command -v qemu-system-x86_64 >/dev/null || exit 1
# Fail closed: evidence files are unique per boot and must never
# truncate an existing host file, including a dangling symlink.
# Refuse aliasing between serial, stderr, host evidence and the PCAP.
declare -A evidence_paths=()
for evidence in "$LOG" "$LOG.stderr" "$LOG.tcp-host" "$LOG.http-host" "$PCAP"; do
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
  "${trace_args[@]}" \
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
      if [[ "$IRQ_TRACE" == 1 ]]; then
        # Guest-side binding and line routing are checked independently
        # from QEMU's device-side notification trace. Neither proves ISR execution.
        python3 "$ROOT/tests/verify_3095_e1000e_irq_binding.py" "$LOG"
        # This isolated QEMU run must show at least one device-side IRQ
        # notification, not merely pending interrupt bits or successful
        # polling-based packet delivery. Guest ISR execution is STILL unproven.
        python3 "$ROOT/tests/verify_3095_e1000e_irq_trace.py" \
          "$LOG.stderr" --require-notify
      fi
      echo "[NET3095-N6-QEMU] PASS two E1000E HTTP/1.0 GET/200 TCP sessions + DNS/DHCP/ICMP + 8s stability; no HTTPS or production network"
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
