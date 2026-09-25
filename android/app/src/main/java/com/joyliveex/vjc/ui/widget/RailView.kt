package com.joyliveex.vjc.ui.widget

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.RectF
import android.view.View
import android.view.ViewGroup
import com.joyliveex.vjc.controller.Buttons

/**
 * A single Joy-Con rail. All elements are declared in the same normalized
 * design space (236 x 500) used by the desktop app, so both ends look alike.
 * Layout is fully proportional to the available height — on small phones the
 * rails shrink, on tablets they grow, and nothing ever leaves the screen
 * (ControllerActivity fits the pair into the measured window first).
 */
class RailView(ctx: Context, private val side: Side) : ViewGroup(ctx) {

    enum class Side { LEFT, RIGHT }

    private data class Spec(val view: View, val x: Float, val y: Float, val w: Float, val h: Float)

    private val specs = ArrayList<Spec>()
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG)
    private val rect = RectF()

    val buttons = ArrayList<PadButton>()
    var stick: StickView? = null
        private set

    companion object {
        const val DW = 236f
        const val DH = 500f
    }

    init {
        if (side == Side.LEFT) buildLeft() else buildRight()
    }

    private fun add(v: View, x: Float, y: Float, w: Float, h: Float) {
        addView(v)
        specs.add(Spec(v, x, y, w, h))
    }

    private fun btn(label: String, mask: Int, kind: PadButton.Kind, x: Float, y: Float, w: Float, h: Float, color: Int? = null): PadButton {
        val b = PadButton(context, label, mask, kind, color ?: Color.parseColor("#E8EAF0"))
        add(b, x, y, w, h)
        buttons.add(b)
        return b
    }

    private fun buildLeft() {
        btn("ZL", Buttons.ZL, PadButton.Kind.PILL, 84f, 0f, 74f, 26f, Color.parseColor("#9AA0AE"))
        btn("L", Buttons.L, PadButton.Kind.PILL, 84f, 10f, 74f, 22f)
        btn("−", Buttons.MINUS, PadButton.Kind.SMALL, 158f, 70f, 44f, 18f)
        val s = StickView(context, true)
        add(s, 58f, 112f, 120f, 120f)
        stick = s
        btn("L3", Buttons.L3, PadButton.Kind.CIRCLE, 172f, 208f, 34f, 34f, Color.parseColor("#9AA0AE"))
        btn("◻", Buttons.CAPTURE, PadButton.Kind.SQUARE, 172f, 258f, 30f, 30f, Color.parseColor("#9AA0AE"))
        btn("▲", Buttons.DPAD_UP, PadButton.Kind.CROSS_V, 94f, 312f, 48f, 56f)
        btn("▼", Buttons.DPAD_DOWN, PadButton.Kind.CROSS_V, 94f, 368f, 48f, 56f)
        btn("◀", Buttons.DPAD_LEFT, PadButton.Kind.CROSS_H, 66f, 336f, 56f, 48f)
        btn("▶", Buttons.DPAD_RIGHT, PadButton.Kind.CROSS_H, 114f, 336f, 56f, 48f)
        btn("SL", Buttons.SL_L, PadButton.Kind.SQUARE, 6f, 150f, 22f, 56f, Color.parseColor("#9AA0AE"))
        btn("SR", Buttons.SR_L, PadButton.Kind.SQUARE, 6f, 214f, 22f, 56f, Color.parseColor("#9AA0AE"))
    }

    private fun buildRight() {
        btn("ZR", Buttons.ZR, PadButton.Kind.PILL, 78f, 0f, 74f, 26f, Color.parseColor("#9AA0AE"))
        btn("R", Buttons.R, PadButton.Kind.PILL, 78f, 10f, 74f, 22f)
        btn("+", Buttons.PLUS, PadButton.Kind.SMALL, 34f, 70f, 44f, 18f)
        btn("X", Buttons.X, PadButton.Kind.CIRCLE, 103f, 28f, 46f, 46f, Color.parseColor("#64B5F6"))
        btn("B", Buttons.B, PadButton.Kind.CIRCLE, 176f, 78f, 46f, 46f, Color.parseColor("#E57373"))
        btn("A", Buttons.A, PadButton.Kind.CIRCLE, 103f, 128f, 46f, 46f, Color.parseColor("#EF5350"))
        btn("Y", Buttons.Y, PadButton.Kind.CIRCLE, 30f, 78f, 46f, 46f, Color.parseColor("#81C784"))
        val s = StickView(context, false)
        add(s, 30f, 196f, 120f, 120f)
        stick = s
        btn("R3", Buttons.R3, PadButton.Kind.CIRCLE, 172f, 248f, 34f, 34f, Color.parseColor("#9AA0AE"))
        btn("◎", Buttons.HOME, PadButton.Kind.CIRCLE, 170f, 306f, 38f, 38f, Color.parseColor("#9AA0AE"))
        btn("SL", Buttons.SL_R, PadButton.Kind.SQUARE, 208f, 150f, 22f, 56f, Color.parseColor("#9AA0AE"))
        btn("SR", Buttons.SR_R, PadButton.Kind.SQUARE, 208f, 214f, 22f, 56f, Color.parseColor("#9AA0AE"))
    }

    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        // fixed aspect 236:500 — fit to whatever the parent gives us
        val w = MeasureSpec.getSize(widthMeasureSpec)
        val h = MeasureSpec.getSize(heightMeasureSpec)
        val useH = h.takeIf { it > 0 } ?: (w * DH / DW).toInt()
        val useW = if (h > 0) (useH * DW / DH).toInt() else w
        setMeasuredDimension(useW, useH)
    }

    override fun onLayout(changed: Boolean, l: Int, t: Int, r: Int, b: Int) {
        val w = (r - l).toFloat()
        val h = (b - t).toFloat()
        for (s in specs) {
            val cl = (s.x / DW * w).toInt()
            val ct = (s.y / DH * h).toInt()
            val cr = cl + (s.w / DW * w).toInt()
            val cb = ct + (s.h / DH * h).toInt()
            s.view.layout(cl, ct, cr, cb)
        }
    }

    override fun dispatchDraw(canvas: Canvas) {
        // body first (below children)
        val w = width.toFloat()
        val h = height.toFloat()
        paint.style = Paint.Style.FILL
        paint.color = Color.parseColor("#2C303A")
        rect.set(0f, 0f, w, h)
        val path = android.graphics.Path()
        path.addRoundRect(rect, floatArrayOf(
            if (side == Side.LEFT) w * 0.44f else w * 0.08f, if (side == Side.LEFT) w * 0.44f else w * 0.08f,
            if (side == Side.LEFT) w * 0.08f else w * 0.44f, if (side == Side.LEFT) w * 0.08f else w * 0.44f,
            if (side == Side.LEFT) w * 0.44f else w * 0.08f, if (side == Side.LEFT) w * 0.44f else w * 0.08f,
            if (side == Side.LEFT) w * 0.08f else w * 0.44f, if (side == Side.LEFT) w * 0.08f else w * 0.44f,
        ), android.graphics.Path.Direction.CCW)
        canvas.drawPath(path, paint)
        paint.style = Paint.Style.STROKE
        paint.strokeWidth = 2f
        paint.color = Color.parseColor("#3A3F4B")
        canvas.drawPath(path, paint)
        super.dispatchDraw(canvas)
    }
}
