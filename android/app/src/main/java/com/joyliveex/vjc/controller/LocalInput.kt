package com.joyliveex.vjc.controller

import android.os.SystemClock
import com.joyliveex.vjc.config.Prefs

/**
 * The phone's own touch controls feed state here (single source of truth for the
 * local controller). The service drains it; the UI reads `pcState` for visual
 * echo of whatever the PC is currently pressing.
 *
 * Multitouch guarantee: every view tracks its own pointer id (assigned by the
 * Android view system), so a finger on the left stick can never "leak" into a
 * button press and vice versa.
 */
object LocalInput {

    private val state = ControllerState()
    val pcState = ControllerState()   // echo of PC input (read-only for UI)
    @Volatile var dirty = false
        private set
    @Volatile var generation = 0L
        private set

    private val curveOut = FloatArray(2)

    fun curve(): StickCurve = StickCurve(
        deadzone = Prefs.deadzone,
        sensitivity = Prefs.sensitivity,
        maxIntensity = Prefs.maxIntensity,
        invertY = Prefs.invertY,
        curve = Prefs.curve,
    )

    @Synchronized
    fun setButton(mask: Int, down: Boolean) {
        if (down) state.buttons = state.buttons or mask else state.buttons = state.buttons and mask.inv()
        // ZL/ZR also drive the analog trigger so games reading LTRIGGER see them
        if (mask == Buttons.ZL) state.lt = if (down) 1f else 0f
        if (mask == Buttons.ZR) state.rt = if (down) 1f else 0f
        touch()
    }

    @Synchronized
    fun setStick(left: Boolean, rawX: Float, rawY: Float) {
        curve().process(rawX, rawY, curveOut)
        if (left) { state.lx = curveOut[0]; state.ly = curveOut[1] }
        else { state.rx = curveOut[0]; state.ry = curveOut[1] }
        touch()
    }

    @Synchronized
    fun releaseAll() {
        state.buttons = 0
        state.lx = 0f; state.ly = 0f; state.rx = 0f; state.ry = 0f
        state.lt = 0f; state.rt = 0f
        touch()
    }

    private fun touch() {
        state.tsMs = SystemClock.elapsedRealtime()
        dirty = true
        generation++
    }

    /** Service drains changed state; returns null when idle & unchanged. */
    @Synchronized
    fun drainIfNeeded(lastSent: ControllerState): ControllerState? {
        if (!dirty) return null
        dirty = false
        val snap = state.snapshot()
        if (snap.sameContent(lastSent) && snap.isIdle) return null
        lastSent.copyFrom(snap)
        return snap
    }

    @Synchronized
    fun snapshot(): ControllerState = state.snapshot()

    fun applyPcState(s: ControllerState) {
        pcState.copyFrom(s)
    }
}
