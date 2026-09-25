# Architecture

## Threading & data flow (critical path in bold)

```
[WPF UI thread]                      [worker threads]                    [network]
VirtualButton/StickControl ──► LocalInput-equivalent: ControllerHub.Set* ─► snapshot
KeyboardMap/GamepadSource ───►   (UI + pollers share ONE state object)      │ 125 Hz + immediate
      ▲                                                                      ▼
      └── StatusChanged ◄── LinkManager/VjcLink event pump ◄── UdpPump/Transport ──► UDP | TCP(adb) | relay
```

- **Input → send**: `ControllerHub` owns a lock-guarded `ControllerState` with
  `Interlocked`-style dirty flag; the sender loop samples at `SendHz` and pushes
  immediately when a button changed (≤ 8 ms floor). No thread ever touches WPF;
  WPF never touches sockets (events are marshalled by the LinkManager dispatcher).
- **Receive → apply**: `VjcLink` runs its own receive/dispatch thread per transport;
  state from the phone merges as the *phone input source* (priority: last active
  source wins per field group, mirroring a real two-controller setup).
- **Daemon path**: STATE_S packets flow PC → (adb reverse TCP) → daemon →
  `injectInputEvent`. The daemon applies its own smoothing (6 ms) so 125 Hz input
  still produces a natural 165 Hz motion stream for picky engines.

## Module responsibilities

| Module | Owns | Never does |
|---|---|---|
| `ControllerModel` | state struct, bit flags, stick curve math, hat | IO, UI |
| `Configuration` | settings JSON (atomic), keyboard/gamepad maps | anything runtime |
| `Network` | wire codec, crypto, link state machine, UDP pump, relay protocol, discovery | WPF, Android |
| `UsbAdb` | adb detection/parsing, reverse, daemon lifecycle | protocol payloads |
| `InputEngine` | XInput polling, keyboard mapping application | sending |
| `VirtualController` | hub: merges UI/pad/kbd sources, drives links, safety release | rendering |
| `Diagnostics` | ring logger, EWMA stats, FPS/loss | business logic |
| `DesktopApp` | WPF shell, rails UI, wizard, settings | raw sockets (delegates) |
| `tools/VjcRelay` | stateless UDP room forwarder | crypto (blind relay) |
| `android/daemon` | shell-uid input injection over injected InputManager API | UI |
| `android/app` | rails UI, link mirror, a11y fallback, test/diagnostics | silent installs |

## Reliability mechanics (anti stuck-state contract)

1. Edge-driven `buttonState`: a button is "down" only between a real DOWN and a real
   UP; release-all paths: window deactivate (WPF `ReleaseAll` on LostFocus),
   service stop, `CMD RELEASE_ALL`, link close (both sides), daemon 3 s input
   timeout, Activity `onPause`.
2. Sequence window: duplicate/replayed packets dropped; gap counted as loss (shown in
   the diagnostics screen).
3. Heartbeat + `lastRxMs` timeout ⇒ link considered dead ⇒ transport backoff
   reconnect (1→15 s) with full re-handshake; UI shows state per transport.
4. Ping runs over the *encrypted* channel (PING/PONG with unix-ms timestamp) — the
   latency you see includes crypto+transport, i.e. it's honest.

## Latency budget (measured targets)

| hop | cost |
|---|---|
| UI input → hub snapshot | < 0.1 ms |
| snapshot → UDP datagram | ≤ 8 ms (change-immediate path bypasses the 8 ms tick) |
| UDP LAN RTT | 0.3–2 ms |
| adb reverse TCP RTT | 0.1–1 ms |
| relay-routed | +1–2 hops of UDP, ~5–40 ms WAN |
| daemon → injected event | < 1 ms (6 ms smoothing window is a tradeoff, not lag) |
