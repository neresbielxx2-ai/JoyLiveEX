package com.joyliveex.vjc.ui

import android.app.Activity
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.graphics.Color
import android.graphics.Typeface
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.widget.Button
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import android.widget.Toast
import com.joyliveex.vjc.ControllerService
import com.joyliveex.vjc.config.Prefs
import com.joyliveex.vjc.diagnostics.EventLog

/** Live diagnostics: transport state, latency, counters, log tail (spec §8/§16). */
class DiagnosticsActivity : Activity() {

    private val main = Handler(Looper.getMainLooper())
    private lateinit var head: TextView
    private lateinit var body: TextView
    private lateinit var scroll: ScrollView

    private val listener: () -> Unit = { main.post { render() } }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        Prefs.init(applicationContext)
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(Color.parseColor("#1B1D22"))
            setPadding(24, 24, 24, 8)
        }
        head = TextView(this).apply {
            setTextColor(Color.parseColor("#9AA0AE"))
            textSize = 12f
            typeface = Typeface.MONOSPACE
        }
        root.addView(head)
        body = TextView(this).apply {
            setTextColor(Color.parseColor("#C8CEDE"))
            textSize = 10f
            typeface = Typeface.MONOSPACE
        }
        scroll = ScrollView(this)
        scroll.addView(body)
        root.addView(scroll, LinearLayout.LayoutParams(-1, 0, 1f).apply { topMargin = 10 })

        val actions = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        actions.addView(Button(this).apply {
            text = "COPIAR"
            setTextColor(Color.parseColor("#E8EAF0"))
            setBackgroundColor(Color.parseColor("#2C303A"))
            setOnClickListener {
                val cm = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
                cm.setPrimaryClip(ClipData.newPlainText("vjc", EventLog.snapshot()))
                Toast.makeText(this@DiagnosticsActivity, "copiado", Toast.LENGTH_SHORT).show()
            }
        }, LinearLayout.LayoutParams(0, 110, 1f))
        actions.addView(Button(this).apply {
            text = "REFRESCAR"
            setTextColor(Color.parseColor("#E8EAF0"))
            setBackgroundColor(Color.parseColor("#2C303A"))
            setOnClickListener { render() }
        }, LinearLayout.LayoutParams(0, 110, 1f).apply { marginStart = 8 })
        root.addView(actions)
        setContentView(root)
        EventLog.listeners.add(listener)
        main.post(ticker)
        render()
    }

    private val ticker = object : Runnable {
        override fun run() {
            render()
            main.postDelayed(this, 800)
        }
    }

    private fun render() {
        val link = ControllerService.link
        head.text = buildString {
            append("estado: "); append(if (ControllerService.connected) "● CONECTADO" else "○ desconectado")
            append("\ntransporte: ").append(ControllerService.transportDesc)
            append(if (ControllerService.isDirect) " (P2P direto)" else "")
            append("\nping: ").append(link?.rttMs?.let { String.format("%.1f ms", it) } ?: "--")
            append("\nperda: ").append(link?.lossPct?.let { String.format("%.2f%%", it) } ?: "--")
            append("\nmodo entrada: ").append(when (Prefs.injectMode) { 2 -> "toque/A11Y" else -> "in-app" })
            append("\nauto-reconnect: ").append(Prefs.autoReconnect)
            append("\nporta: ").append(Prefs.pcPort).append(" · host: ").append(if (Prefs.host.isEmpty()) "—" else Prefs.host)
        }
        body.text = EventLog.snapshot()
        scroll.post { scroll.fullScroll(android.view.View.FOCUS_DOWN) }
    }

    override fun onDestroy() {
        EventLog.listeners.remove(listener)
        super.onDestroy()
    }
}
