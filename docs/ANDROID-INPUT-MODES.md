# Android input modes — the honest engineering answer

The brief asks for "the maximum possible compatibility with Android games that accept
gamepads" while knowing **a normal APK cannot magically create a physical global
device**. It's right to demand that. Here is exactly what is possible and what the
project implements.

## Why a plain APK can't do it

| Mechanism | Reality |
|---|---|
| `InputManager.injectInputEvent` | needs `INJECT_EVENTS` (signature\|privileged) → **not grantable** to a normal app (not via `pm grant`, not via appops) |
| Creating an HID device (USB gadget / BT HID) | requires system-level control of the USB/BT stack; no public API for an app to expose itself as a gamepad to *itself* |
| `InputManager` "virtual devices" (`UinputController`, `cmd input add-device` — Android 14) | shell/system-only, not callable by normal apps |
| Accessibility | can dispatch **gestures/touch** (public since API 24: `dispatchGesture`) and global actions — **not** key/motion gamepad events into other apps |
| Shizuku (`adb`-granted shell binder) | viable on-device path, no PC needed after setup — documented below as an extension point |

## Mode 1 — GLOBAL GAMEPAD via ADB daemon  ✅ (implemented, primary)

Architecture proven in the wild by scrcpy / DeskDock: the PC pushes a **DEX jar** to
`/data/local/tmp` and runs it as the **adb `shell` uid**:

```
adb shell CLASSPATH=/data/local/tmp/vjc-daemon.jar app_process / com.joyliveex.vjc.daemon.Main <port> <code>
```

`shell` holds the input-injection permission that the `input` command itself uses, so
the daemon calls `InputManager.injectInputEvent` for:

- `KeyEvent` with `KEYCODE_BUTTON_A/B/X/Y`, `L1/R1` (L/R), `L2/R2` (ZL/ZR),
  `THUMBL/THUMBR`, `START/SELECT` (+/−), `DPAD_*`, `HOME`, `GUIDE` — constructed with
  `source = SOURCE_GAMEPAD | SOURCE_JOYSTICK`;
- `MotionEvent` (Builder, `setSource(SOURCE_GAMEPAD)`) carrying `AXIS_X/AXIS_Y`
  (left stick), `AXIS_Z/AXIS_RZ` (right stick), `AXIS_LTRIGGER/AXIS_RTRIGGER`,
  `AXIS_HAT_X/AXIS_HAT_Y` and the matching `buttonState` mask — the same shape a real
  driver produces.

Every framework app and game that reads generic input (Unity legacy + new Input
System, Unreal, Godot, libGDX, native ALOOPER callbacks…) sees a gamepad event stream
as soon as it has focus. The **CONTROLLER TEST** screen in the companion app receives
the exact same events, which is how you verify it on-device.

Limitations (stated, not hidden):
- the injected device id is "virtual" (-1): engines that *enumerate* `InputDevice`s
  looking for a gamepad before enabling controls may not light their "controller
  connected" UI. Games that listen to key/motion events work. This is an Android
  framework limitation for shell injection; on Android 14 the system's virtual-input
  device APIs make a full-featured device possible (see roadmap).
- requires the PC + USB + adb for the session (that's the product concept);
- OEM ROMs that kill background adb processes are rare but exist.

## Mode 2 — TOUCH MAPPING via Accessibility ✅ (implemented, no-PC fallback)

The companion receives the same controller stream (LAN/Internet) and converts it with
`AccessibilityService.dispatchGesture`:

- buttons → press/hold at user-mapped screen points (hold = long stroke released by a
  continuation stroke);
- sticks → short drag gestures around a mapped anchor;
- HOME/MINUS → global actions.

This is precisely how the classic "gamepad for games without gamepad support" apps
work. Games that block overlays/automation may ignore it (their policy, not ours).
No root, no PC daemon; requires one tap in **Settings → Accessibility** to enable
"Virtual Joy-Con".

## Mode 3 — IN-APP test ✅ (implemented)

Without any privilege, events reach *our own windows*. Used by the pairing/latency
flow and to demo the pipeline; explicitly labeled "does not affect games".

## Roadmap (documented extension points, not vaporware claims)

- **Shizuku** mode: same injection code path, but the *phone* talks to the Shizuku
  shell binder — wireless-debugging pairing on device and the PC becomes optional.
  The app already abstracts injection behind `InputRouter`; a Shizuku client is ~1
  service binding.
- **Android 14 `cmd input add-device`**: creates a *real* kernel-visible virtual
  joystick via shell (uinput); the daemon could register `VJC Virtual Controller`
  with proper `InputDevice` enumeration once it's stable across OEM builds.
- Root/KSU module exposing a permanent uinput device — out of scope (by design).
