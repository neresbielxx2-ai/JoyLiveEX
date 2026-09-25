package com.joyliveex.vjc.ui

import android.app.Activity
import android.graphics.Color
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.view.Gravity
import android.view.View
import android.view.WindowManager
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.TextView
import com.joyliveex.vjc.ControllerService
import com.joyliveex.vjc.config.Prefs
import com.joyliveex.vjc.controller.LocalInput
import com.joyliveex.vjc.ui.widget.RailView

/**
 * The on-screen virtual Joy-Cons (spec §12). On first open the pair is
 * CENTERED (not glued to the edges) with a proportional fit; spacing, scale
 * and opacity are adjustable in Settings. Multitouch: left stick, right
 * stick and every button track independent pointers.
 */
class ControllerActivity : Activity() {

    private val main = Handler(Looper.getMainLooper())
    private lateinit var left: RailView
    private lateinit var right: RailView
    private lateinit var status: TextView
    private val echo = com.joyliveex.vjc.controller.ControllerState()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        window.setFlags(
            WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON,
            WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON,
        )
        Prefs.init(applicationContext)

        val root = FrameLayout(this).apply { setBackgroundColor(Color.parseColor("#12141A")) }

        val row = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER
            alpha = Prefs.opacity.coerceIn(0.35f, 1f)
        }
        left = RailView(this, RailView.Side.LEFT)
        right = RailView(this, RailView.Side.RIGHT)
        row.addView(left)
        row.addView(View(this), LinearLayout.LayoutParams(Prefs.spacing.toInt().coerceAtLeast(0), 1))
        row.addView(right)

        val scaler = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER
        }
        scaler.addView(row, LinearLayout.LayoutParams(-1, -1))
        root.addView(scaler, FrameLayout.LayoutParams(-1, -1))

        status = TextView(this).apply {
            setTextColor(Color.parseColor("#9AA0AE"))
            textSize = 11f
            setBackgroundColor(0x88000000.toInt())
            setPadding(24, 8, 24, 8)
            gravity = Gravity.CENTER_HORIZONTAL
        }
        root.addView(status, FrameLayout.LayoutParams(-1, -2, Gravity.TOP))

        val exit = TextView(this).apply {
            text = "✕  voltar"
            setTextColor(Color.parseColor("#E8EAF0"))
            textSize = 13f
            setPadding(32, 24, 32, 24)
            setBackgroundColor(0x88000000.toInt())
            setOnClickListener { finish() }
        }
        root.addView(exit, FrameLayout.LayoutParams(-2, -2, Gravity.TOP or Gravity.START))

        setContentView(root)
        ControllerService.listeners.add(statusRefresher)
        main.post(echoTick)
    }

    private val statusRefresher: () -> Unit = { updateStatus() }

    private fun updateStatus() {
        val link = ControllerService.link
        status.text = when {
            ControllerService.connected ->
                "● PC conectado · ${ControllerService.transportDesc} · ping ${link?.rttMs?.toInt() ?: 0} ms · perda ${String.format("%.1f", link?.lossPct ?: 0.0)}%"
            else -> "○ PC desconectado — abrir PAREAMENTO (o serviço tenta reconectar sozinho)"
        }
    }

    private val echoTick = object : Runnable {
        override fun run() {
            // visual echo: PC-side presses light the buttons on our rails too
            val pc = LocalInput.pcState
            for (rail in listOf(left, right)) {
                for (b in rail.buttons) {
                    b.setEchoPressed((pc.buttons and b.mask) != 0)
                }
            }
            updateStatus()
            main.postDelayed(this, 100)
        }
    }

    override fun onDestroy() {
        ControllerService.listeners.remove(statusRefresher)
        main.removeCallbacks(echoTick)
        LocalInput.releaseAll() // leaving the screen zeroes sticks/buttons — no stuck input
        super.onDestroy()
    }

    override fun onPause() {
        LocalInput.releaseAll()
        super.onPause()
    }
}
