package com.joyliveex.vjc.controller

/** Button bit layout — mirror of ButtonFlags.cs / Constants.java. Do not renumber! */
object Buttons {
    const val A = 1
    const val B = 1 shl 1
    const val X = 1 shl 2
    const val Y = 1 shl 3
    const val L = 1 shl 4
    const val R = 1 shl 5
    const val ZL = 1 shl 6
    const val ZR = 1 shl 7
    const val MINUS = 1 shl 8
    const val PLUS = 1 shl 9
    const val HOME = 1 shl 10
    const val CAPTURE = 1 shl 11
    const val L3 = 1 shl 12
    const val R3 = 1 shl 13
    const val DPAD_UP = 1 shl 14
    const val DPAD_DOWN = 1 shl 15
    const val DPAD_LEFT = 1 shl 16
    const val DPAD_RIGHT = 1 shl 17
    const val SL_L = 1 shl 18
    const val SR_L = 1 shl 19
    const val SL_R = 1 shl 20
    const val SR_R = 1 shl 21

    const val HAT_NONE = 0
    const val HAT_N = 1
    const val HAT_NE = 2
    const val HAT_E = 3
    const val HAT_SE = 4
    const val HAT_S = 5
    const val HAT_SW = 6
    const val HAT_W = 7
    const val HAT_NW = 8

    fun hatOf(b: Int): Int {
        val up = b and DPAD_UP != 0
        val down = b and DPAD_DOWN != 0
        val left = b and DPAD_LEFT != 0
        val right = b and DPAD_RIGHT != 0
        return when {
            up && right -> HAT_NE
            up && left -> HAT_NW
            down && right -> HAT_SE
            down && left -> HAT_SW
            up -> HAT_N
            down -> HAT_S
            left -> HAT_W
            right -> HAT_E
            else -> HAT_NONE
        }
    }
}

/** Full controller state; axes normalized -1..1 (Y down positive), triggers 0..1. */
class ControllerState {
    @JvmField var buttons = 0
    @JvmField var lx = 0f
    @JvmField var ly = 0f
    @JvmField var rx = 0f
    @JvmField var ry = 0f
    @JvmField var lt = 0f
    @JvmField var rt = 0f
    @JvmField var tsMs = 0L

    fun sameContent(o: ControllerState): Boolean =
        buttons == o.buttons &&
            toShort(lx) == toShort(o.lx) && toShort(ly) == toShort(o.ly) &&
            toShort(rx) == toShort(o.rx) && toShort(ry) == toShort(o.ry) &&
            toByte(lt) == toByte(o.lt) && toByte(rt) == toByte(o.rt)

    fun copyFrom(o: ControllerState) {
        buttons = o.buttons; lx = o.lx; ly = o.ly; rx = o.rx; ry = o.ry
        lt = o.lt; rt = o.rt; tsMs = o.tsMs
    }

    val isIdle: Boolean
        get() = buttons == 0 && lx == 0f && ly == 0f && rx == 0f && ry == 0f && lt == 0f && rt == 0f

    fun snapshot(): ControllerState {
        val c = ControllerState()
        c.copyFrom(this)
        return c
    }

    companion object {
        fun toShort(v: Float): Short = (v.coerceIn(-1f, 1f) * 32767f).toInt().toShort()
        fun toByte(v: Float): Int = (v.coerceIn(0f, 1f) * 255f).toInt()
    }
}

/** Stick processing: deadzone/sensitivity/curve/invert/max — mirror of StickCurve.cs. */
class StickCurve(
    var deadzone: Float = 0.12f,
    var sensitivity: Float = 1f,
    var maxIntensity: Float = 1f,
    var invertY: Boolean = false,
    var curve: Int = 0,          // 0 linear, 1 gentle, 2 aggressive, 3 custom exp
    var exponent: Float = 1f,
) {
    fun process(rawX: Float, rawY: Float, out: FloatArray) {
        var x = rawX.coerceIn(-1f, 1f)
        var y = rawY.coerceIn(-1f, 1f)
        if (invertY) y = -y
        val mag = Math.hypot(x.toDouble(), y.toDouble()).toFloat()
        if (mag < 1e-6f || mag <= deadzone) {
            out[0] = 0f; out[1] = 0f; return
        }
        val scaled = ((mag - deadzone) / (1f - deadzone)).coerceIn(0f, 1f)
        val t = when (curve) {
            1 -> scaled * scaled * (3f - 2f * scaled)
            2 -> Math.pow(scaled.toDouble(), 0.65).toFloat()
            3 -> Math.pow(scaled.toDouble(), exponent.coerceIn(0.2f, 4f).toDouble()).toFloat()
            else -> scaled
        }
        var nx = x * (t / mag) * sensitivity
        var ny = y * (t / mag) * sensitivity
        val mag2 = Math.hypot(nx.toDouble(), ny.toDouble()).toFloat()
        val cap = maxIntensity.coerceIn(0.05f, 1f)
        if (mag2 > cap) {
            val s = cap / mag2
            nx *= s; ny *= s
        }
        out[0] = nx.coerceIn(-1f, 1f)
        out[1] = ny.coerceIn(-1f, 1f)
    }
}
