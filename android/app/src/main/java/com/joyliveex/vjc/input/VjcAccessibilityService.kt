package com.joyliveex.vjc.input

import android.accessibilityservice.AccessibilityService
import android.accessibilityservice.GestureDescription
import android.graphics.Path
import android.os.Handler
import android.os.Looper
import android.view.accessibility.AccessibilityEvent
import com.joyliveex.vjc.config.Prefs
import com.joyliveex.vjc.diagnostics.EventLog

/**
 * Accessibility-based touch mapping (input mode 2). This is the strongest global
 * option a *normal installed app* has without root/ADB: `dispatchGesture` is a
 * public API and its touch events reach any foreground game. It is NOT a system
 * gamepad device — games that demand a real gamepad only work via the ADB daemon
 * (or Shizuku-style privileges). The app labels each mode honestly.
 *
 * Press/release holds: DOWN starts a long stroke with willContinue=true; UP
 * dispatches the continuing stroke (zero movement), which is the documented way
 * to lift the finger while keeping sequence semantics.
 */
class VjcAccessibilityService : AccessibilityService() {

    companion object {
        @Volatile
        var instance: VjcAccessibilityService? = null
    }

    private val main = Handler(Looper.getMainLooper())
    private val heldPoints = HashMap<String, Pair<Float, Float>>()
    private var lastStickAt = 0L
    private var displayW = 1f
    private var displayH = 1f

    override fun onServiceConnected() {
        super.onServiceConnected()
        instance = this
        val dm = resources.displayMetrics
        displayW = dm.widthPixels.toFloat()
        displayH = dm.heightPixels.toFloat()
        EventLog.add("A11Y", "serviço de acessibilidade conectado (modo toque ativo)")
    }

    override fun onAccessibilityEvent(event: AccessibilityEvent?) { /* no-op */ }
    override fun onInterrupt() {}

    override fun onUnbind(intent: android.content.Intent?): Boolean {
        instance = null
        return super.onUnbind(intent)
    }

    private fun mappingFor(name: String): Pair<Float, Float>? {
        for (entry in Prefs.a11yMapping.split("|")) {
            val kv = entry.split("=", limit = 2)
            if (kv.size != 2) continue
            if (kv[0].trim().uppercase() != name) continue
            val xy = kv[1].trim().split(",")
            if (xy.size != 2) continue
            val x = xy[0].toFloatOrNull() ?: continue
            val y = xy[1].toFloatOrNull() ?: continue
            return x to y
        }
        return null
    }

    fun press(name: String) {
        if (name == "HOME") {
            main.post { try { performGlobalAction(GLOBAL_ACTION_HOME) } catch (e: Exception) { } }
            return
        }
        if (name == "MINUS") {
            main.post { try { performGlobalAction(GLOBAL_ACTION_BACK) } catch (e: Exception) { } }
            return
        }
        val m = mappingFor(name) ?: return
        heldPoints[name] = m
        main.post {
            val path = Path().apply { moveTo(m.first * displayW, m.second * displayH) }
            val stroke = GestureDescription.StrokeDescription(path, 0L, 60_000L, true)
            try {
                dispatchGesture(GestureDescription.Builder().addStroke(stroke).build(), null, null)
            } catch (e: Exception) {
                EventLog.add("A11Y", "gesture falhou: ${e.message}")
            }
        }
    }

    fun release(name: String) {
        val m = heldPoints.remove(name) ?: return
        main.post {
            val path = Path().apply { moveTo(m.first * displayW, m.second * displayH) }
            // "continued" stroke lifts the finger at the same point
            val stroke = GestureDescription.StrokeDescription(path, 0L, 20L, false)
            try {
                dispatchGesture(GestureDescription.Builder().addStroke(stroke).build(), null, null)
            } catch (e: Exception) { }
        }
    }

    fun sticks(lx: Float, ly: Float, rx: Float, ry: Float) {
        val now = System.currentTimeMillis()
        if (now - lastStickAt < 70) return
        lastStickAt = now
        mappingFor("LSTICK")?.let { if (Math.abs(lx) > 0.05 || Math.abs(ly) > 0.05) dragFrom(it, lx, ly) }
        mappingFor("RSTICK")?.let { if (Math.abs(rx) > 0.05 || Math.abs(ry) > 0.05) dragFrom(it, rx, ry) }
    }

    private fun dragFrom(anchor: Pair<Float, Float>, dx: Float, dy: Float) {
        val sx = anchor.first * displayW
        val sy = anchor.second * displayH
        val r = displayH * 0.16f
        val ex = (sx + dx * r).coerceIn(0f, displayW)
        val ey = (sy + dy * r).coerceIn(0f, displayH)
        main.post {
            val path = Path().apply {
                moveTo(sx, sy)
                lineTo(ex, ey)
            }
            val stroke = GestureDescription.StrokeDescription(path, 0L, 60L)
            try { dispatchGesture(GestureDescription.Builder().addStroke(stroke).build(), null, null) }
            catch (e: Exception) { }
        }
    }

    fun vibrate(ms: Long) {
        try {
            val vib = getSystemService(VIBRATOR_SERVICE) as? android.os.Vibrator
            @Suppress("DEPRECATION")
            vib?.vibrate(ms)
        } catch (e: Exception) { }
    }
}
