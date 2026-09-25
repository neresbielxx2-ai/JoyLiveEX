package com.joyliveex.vjc.input

import com.joyliveex.vjc.config.Prefs
import com.joyliveex.vjc.controller.ControllerState
import com.joyliveex.vjc.diagnostics.EventLog

/**
 * Decides WHAT the phone does with controller events it receives from the PC.
 *
 * Mode reality check (docs/ANDROID-INPUT-MODES.md):
 *  0 NONE          — do nothing locally (events still reach the ADB daemon, which
 *                    injects globally by itself — the app is not in that path).
 *  1 IN_APP        — events are visible to the companion's own screens (test/UI).
 *                    An ordinary APK cannot inject into OTHER apps; no fake claims.
 *  2 A11Y_TOUCH    — AccessibilityService translates the gamepad stream into touch
 *                    gestures over a user-mapped layout: the best non-root, non-ADB
 *                    global option (works with touch-only games).
 */
object InputRouter {

    private var prev = ControllerState()

    fun onPcState(s: ControllerState) {
        when (Prefs.injectMode) {
            2 -> routeToAccessibility(s)
            else -> { /* 0: nothing; 1: LocalInput.pcState already updated by the service */ }
        }
        prev = s
    }

    private fun routeToAccessibility(s: ControllerState) {
        val svc = VjcAccessibilityService.instance ?: return
        val diff = s.buttons xor prev.buttons
        if (diff != 0) {
            for (bit in 0 until 32) {
                val mask = 1 shl bit
                if (diff and mask == 0) continue
                val name = nameOf(mask) ?: continue
                if (s.buttons and mask != 0) svc.press(name) else svc.release(name)
            }
        }
        svc.sticks(s.lx, s.ly, s.rx, s.ry)
    }

    private fun nameOf(mask: Int): String? = when (mask) {
        com.joyliveex.vjc.controller.Buttons.A -> "A"
        com.joyliveex.vjc.controller.Buttons.B -> "B"
        com.joyliveex.vjc.controller.Buttons.X -> "X"
        com.joyliveex.vjc.controller.Buttons.Y -> "Y"
        com.joyliveex.vjc.controller.Buttons.L -> "L"
        com.joyliveex.vjc.controller.Buttons.R -> "R"
        com.joyliveex.vjc.controller.Buttons.ZL -> "ZL"
        com.joyliveex.vjc.controller.Buttons.ZR -> "ZR"
        com.joyliveex.vjc.controller.Buttons.MINUS -> "MINUS"
        com.joyliveex.vjc.controller.Buttons.PLUS -> "PLUS"
        com.joyliveex.vjc.controller.Buttons.HOME -> "HOME"
        com.joyliveex.vjc.controller.Buttons.CAPTURE -> "CAPTURE"
        com.joyliveex.vjc.controller.Buttons.L3 -> "L3"
        com.joyliveex.vjc.controller.Buttons.R3 -> "R3"
        com.joyliveex.vjc.controller.Buttons.DPAD_UP -> "UP"
        com.joyliveex.vjc.controller.Buttons.DPAD_DOWN -> "DOWN"
        com.joyliveex.vjc.controller.Buttons.DPAD_LEFT -> "LEFT"
        com.joyliveex.vjc.controller.Buttons.DPAD_RIGHT -> "RIGHT"
        else -> null
    }

    fun vibrate(ms: Int) {
        val svc = VjcAccessibilityService.instance
        if (svc != null) svc.vibrate(ms.toLong())
    }
}
