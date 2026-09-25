package com.joyliveex.vjc.ui.widget

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.RectF
import android.view.MotionEvent
import android.view.View
import com.joyliveex.vjc.controller.LocalInput

/**
 * Press/release button with per-view pointer tracking (multitouch safe):
 * once a finger claims this view (trackId), other fingers cannot interfere,
 * and ACTION_UP for that pointer id always releases — no stuck states.
 */
class PadButton(
    ctx: Context,
    val label: String,
    val mask: Int,
    val kind: Kind = Kind.CIRCLE,
    val glyphColor: Int = Color.parseColor("#E8EAF0"),
) : View(ctx) {

    enum class Kind { CIRCLE, PILL, SQUARE, CROSS_H, CROSS_V, SMALL }

    private var trackId = -1
    private var visualPressed = false
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG)
    private val textPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        textAlign = Paint.Align.CENTER
        color = glyphColor
        isFakeBoldText = true
    }
    private val rect = RectF()

    fun setEchoPressed(p: Boolean) {
        if (visualPressed != p) {
            visualPressed = p
            invalidate()
        }
    }

    @SuppressLint("ClickableViewAccessibility")
    override fun onTouchEvent(event: MotionEvent): Boolean {
        val idx = event.actionIndex
        val pid = event.getPointerId(idx)
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN, MotionEvent.ACTION_POINTER_DOWN -> {
                if (trackId == -1) {
                    trackId = pid
                    LocalInput.setButton(mask, true)
                    invalidate()
                    performHapticFeedback(android.view.HapticFeedbackConstants.VIRTUAL_KEY)
                }
            }
            MotionEvent.ACTION_UP, MotionEvent.ACTION_POINTER_UP, MotionEvent.ACTION_CANCEL -> {
                if (pid == trackId || event.actionMasked == MotionEvent.ACTION_CANCEL) {
                    if (trackId != -1) {
                        trackId = -1
                        LocalInput.setButton(mask, false)
                        invalidate()
                    }
                }
            }
        }
        return true
    }

    override fun onDetachedFromWindow() {
        if (trackId != -1) {
            trackId = -1
            LocalInput.setButton(mask, false) // never leave pressed on screen rotation/exit
        }
        super.onDetachedFromWindow()
    }

    override fun onDraw(c: Canvas) {
        val pressed = visualPressed || trackId != -1
        val w = width.toFloat()
        val h = height.toFloat()
        paint.color = if (pressed) Color.parseColor("#4FC3F7") else Color.parseColor("#3A4150")
        paint.style = Paint.Style.FILL
        paint.strokeWidth = 2f
        when (kind) {
            Kind.CIRCLE -> {
                c.drawCircle(w / 2, h / 2, Math.min(w, h) / 2 - 1, paint)
                paint.color = Color.parseColor("#3A3F4B")
                paint.style = Paint.Style.STROKE
                c.drawCircle(w / 2, h / 2, Math.min(w, h) / 2 - 1, paint)
            }
            Kind.CROSS_H -> {
                rect.set(0f, h * 0.25f, w, h * 0.75f)
                c.drawRoundRect(rect, 12f, 12f, paint)
            }
            Kind.CROSS_V -> {
                rect.set(w * 0.25f, 0f, w * 0.75f, h)
                c.drawRoundRect(rect, 12f, 12f, paint)
            }
            else -> {
                rect.set(0f, 0f, w, h)
                c.drawRoundRect(rect, Math.min(w, h) / 2.6f, Math.min(w, h) / 2.6f, paint)
            }
        }
        if (label.isNotEmpty()) {
            textPaint.textSize = Math.min(w, h) * if (kind == Kind.CIRCLE) 0.46f else 0.40f
            c.drawText(label, w / 2, h / 2 - (textPaint.descent() + textPaint.ascent()) / 2, textPaint)
        }
    }
}

/**
 * Analog stick: finger position is clamped to the travel circle; releasing the
 * finger zeroes X/Y. Emits raw -1..1 (curve/deadzone applied downstream in
 * LocalInput via the shared StickCurve).
 */
class StickView(
    ctx: Context,
    val left: Boolean,
) : View(ctx) {

    private var trackId = -1
    private var kx = 0f
    private var ky = 0f
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG)

    private val wellColor = Color.parseColor("#181A1F")
    private val ringColor = Color.parseColor("#3A3F4B")
    private val knobColor = Color.parseColor("#414755")
    private val activeColor = Color.parseColor("#4FC3F7")

    @SuppressLint("ClickableViewAccessibility")
    override fun onTouchEvent(event: MotionEvent): Boolean {
        val idx = event.actionIndex
        val pid = event.getPointerId(idx)
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN, MotionEvent.ACTION_POINTER_DOWN -> {
                if (trackId == -1) {
                    trackId = pid
                    updateFrom(event, idx)
                }
            }
            MotionEvent.ACTION_MOVE -> {
                for (i in 0 until event.pointerCount) {
                    if (event.getPointerId(i) == trackId) updateFrom(event, i)
                }
            }
            MotionEvent.ACTION_UP, MotionEvent.ACTION_POINTER_UP, MotionEvent.ACTION_CANCEL -> {
                if (pid == trackId || event.actionMasked == MotionEvent.ACTION_CANCEL) {
                    trackId = -1
                    kx = 0f; ky = 0f
                    LocalInput.setStick(left, 0f, 0f)
                    invalidate()
                }
            }
        }
        return true
    }

    private fun updateFrom(event: MotionEvent, i: Int) {
        val cx = width / 2f
        val cy = height / 2f
        var dx = (event.getX(i) - cx) / (width / 2f)
        var dy = (event.getY(i) - cy) / (height / 2f)
        val mag = Math.hypot(dx.toDouble(), dy.toDouble()).toFloat()
        if (mag > 1f) { dx /= mag; dy /= mag }
        kx = dx; ky = dy
        LocalInput.setStick(left, dx, dy)
        invalidate()
    }

    override fun onDetachedFromWindow() {
        if (trackId != -1) {
            trackId = -1
            LocalInput.setStick(left, 0f, 0f)
        }
        super.onDetachedFromWindow()
    }

    override fun onDraw(c: Canvas) {
        val cx = width / 2f
        val cy = height / 2f
        val r = Math.min(width, height) / 2f - 2f
        paint.style = Paint.Style.FILL
        paint.color = wellColor
        c.drawCircle(cx, cy, r, paint)
        paint.color = ringColor
        paint.style = Paint.Style.STROKE
        paint.strokeWidth = 3f
        c.drawCircle(cx, cy, r, paint)
        val knobR = r * 0.46f
        val kcx = cx + kx * (r - knobR)
        val kcy = cy + ky * (r - knobR)
        paint.style = Paint.Style.FILL
        paint.color = if (trackId != -1) activeColor else knobColor
        c.drawCircle(kcx, kcy, knobR, paint)
        paint.color = wellColor
        c.drawCircle(kcx, kcy, knobR * 0.55f, paint)
    }
}
