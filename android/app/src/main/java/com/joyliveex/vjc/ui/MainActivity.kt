package com.joyliveex.vjc.ui

import android.Manifest
import android.app.Activity
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.provider.Settings
import android.view.Gravity
import android.view.View
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Toast
import com.joyliveex.vjc.ControllerService
import com.joyliveex.vjc.config.Prefs

/** Home screen (spec §11): status, start/stop service, navigation. */
class MainActivity : Activity() {

    private val main = Handler(Looper.getMainLooper())
    private lateinit var dot: View
    private lateinit var statusText: TextView
    private lateinit var subText: TextView
    private lateinit var pingText: TextView
    private lateinit var startBtn: Button

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        Prefs.init(applicationContext)
        buildUi()
        requestNotifPermission()
        ControllerService.listeners.add(refresher)
        if (Prefs.serviceShouldRun && !ControllerService.running) startService()
    }

    private val refresher = Runnable { refresh() }

    private fun bg(): Int = Color.parseColor("#1B1D22")

    private fun card(): GradientDrawable = GradientDrawable().apply {
        setColor(Color.parseColor("#24272E"))
        cornerRadius = 22f
    }

    private fun buildUi() {
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(bg())
            setPadding(40, 56, 40, 40)
            gravity = Gravity.CENTER_HORIZONTAL
        }
        root.addView(TextView(this).apply {
            text = "VIRTUAL JOY-CON"
            textSize = 24f
            setTextColor(Color.parseColor("#E8EAF0"))
            typeface = Typeface.DEFAULT_BOLD
            gravity = Gravity.CENTER
        })
        root.addView(TextView(this).apply {
            text = "companion para Virtual Joy-Con (PC ⇆ Android)"
            textSize = 12f
            setTextColor(Color.parseColor("#9AA0AE"))
            gravity = Gravity.CENTER
            setPadding(0, 6, 0, 24)
        })

        val status = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            background = card()
            setPadding(30, 26, 30, 26)
        }
        val row = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL }
        dot = View(this).apply {
            val d = GradientDrawable().apply { shape = GradientDrawable.OVAL; setColor(Color.parseColor("#9AA0AE")) }
            background = d
            layoutParams = LinearLayout.LayoutParams(26, 26).apply { marginEnd = 16 }
        }
        statusText = TextView(this).apply {
            text = "PC não conectado"
            textSize = 18f
            setTextColor(Color.parseColor("#E8EAF0"))
        }
        row.addView(dot)
        row.addView(statusText)
        status.addView(row)
        subText = TextView(this).apply {
            textSize = 12f
            setTextColor(Color.parseColor("#9AA0AE"))
            setPadding(0, 12, 0, 0)
        }
        status.addView(subText)
        pingText = TextView(this).apply {
            textSize = 12f
            setTextColor(Color.parseColor("#9AA0AE"))
            setPadding(0, 6, 0, 0)
        }
        status.addView(pingText)
        root.addView(status, LinearLayout.LayoutParams(-1, -2).apply { bottomMargin = 20 })

        startBtn = Button(this).apply {
            text = if (ControllerService.running) "PARAR" else "INICIAR"
            setBackgroundColor(Color.parseColor("#2C303A"))
            setTextColor(Color.parseColor("#E8EAF0"))
            setOnClickListener {
                if (ControllerService.running) stopService() else startService()
            }
        }
        root.addView(startBtn, LinearLayout.LayoutParams(-1, 150).apply { bottomMargin = 14 })

        fun nav(label: String, cls: Class<out Activity>, weight: Float = 1f) {
            val b = Button(this).apply {
                text = label
                setBackgroundColor(Color.parseColor("#24272E"))
                setTextColor(Color.parseColor("#E8EAF0"))
                setOnClickListener { startActivity(Intent(this@MainActivity, cls)) }
            }
            root.addView(b, LinearLayout.LayoutParams(-1, 130).apply { bottomMargin = 10 })
        }
        nav("CONTROLES NA TELA", ControllerActivity::class.java)
        nav("PAREAMENTO (LAN / INTERNET / USB)", PairActivity::class.java)
        nav("CONTROLLER TEST", TestActivity::class.java)
        nav("DIAGNÓSTICO", DiagnosticsActivity::class.java)
        nav("CONFIGURAÇÕES", SettingsActivity::class.java)

        setContentView(root)
    }

    private fun startService() {
        val i = Intent(this, ControllerService::class.java)
        i.putExtra(ControllerService.EXTRA, "start")
        try {
            if (Build.VERSION.SDK_INT >= 26) startForegroundService(i) else startService(i)
        } catch (e: Exception) {
            Toast.makeText(this, "não foi possível iniciar o serviço: ${e.message}", Toast.LENGTH_LONG).show()
        }
        main.postDelayed({ refresh() }, 400)
    }

    private fun stopService() {
        val i = Intent(this, ControllerService::class.java)
        i.putExtra(ControllerService.EXTRA, "stop")
        try { startService(i) } catch (e: Exception) { }
        main.postDelayed({ refresh() }, 300)
    }

    private fun requestNotifPermission() {
        if (Build.VERSION.SDK_INT >= 33 &&
            checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) {
            requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 100)
        }
    }

    private fun refresh() {
        val connected = ControllerService.connected
        (dot.background as GradientDrawable).setColor(
            if (connected) Color.parseColor("#66D277") else Color.parseColor("#9AA0AE"),
        )
        statusText.text = if (connected) "PC conectado" else "PC não conectado"
        subText.text = "Método: ${ControllerService.transportDesc} · modo entrada: ${modeName()}"
        val link = ControllerService.link
        pingText.text = if (connected && link != null)
            "Ping: ${link.rttMs.toInt()} ms · perda: ${String.format("%.1f", link.lossPct)}%"
        else "Ping: -- ms"
        startBtn.text = if (ControllerService.running) "PARAR" else "INICIAR"
    }

    private fun modeName(): String = when (Prefs.mode) {
        Prefs.MODE_USB -> "USB (ADB)"
        Prefs.MODE_INTERNET -> "Internet"
        else -> "LAN"
    } + when (Prefs.injectMode) {
        2 -> " + toque/A11Y"
        else -> ""
    }

    override fun onResume() {
        super.onResume()
        main.post(refresher)
    }

    override fun onDestroy() {
        ControllerService.listeners.remove(refresher)
        super.onDestroy()
    }
}
