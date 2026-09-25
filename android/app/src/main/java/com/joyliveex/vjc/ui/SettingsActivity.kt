package com.joyliveex.vjc.ui

import android.app.Activity
import android.content.Intent
import android.graphics.Color
import android.os.Bundle
import android.provider.Settings
import android.view.Gravity
import android.widget.Button
import android.widget.CheckBox
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.RadioButton
import android.widget.RadioGroup
import android.widget.SeekBar
import android.widget.ScrollView
import android.widget.TextView
import com.joyliveex.vjc.config.Prefs
import com.joyliveex.vjc.controller.LocalInput

/** Config (spec §16, device side): controls feel, interface, input mode, a11y mapping. */
class SettingsActivity : Activity() {

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        Prefs.init(applicationContext)
        setContentView(build())
    }

    private fun c(t: String, color: String = "#E8EAF0") = TextView(this).apply {
        text = t
        setTextColor(Color.parseColor(color))
        textSize = 13f
        setPadding(0, 14, 0, 4)
    }

    private fun seekBar(max: Int, progress: Int, onChange: (Int) -> Unit): SeekBar = SeekBar(this).apply {
        this.max = max
        this.progress = progress
        setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
            override fun onProgressChanged(sb: SeekBar?, p: Int, fromUser: Boolean) = onChange(p)
            override fun onStartTrackingTouch(sb: SeekBar?) {}
            override fun onStopTrackingTouch(sb: SeekBar?) {}
        })
    }

    private fun build(): ScrollView {
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(Color.parseColor("#1B1D22"))
            setPadding(36, 30, 36, 36)
        }
        root.addView(TextView(this).apply {
            text = "CONFIGURAÇÕES"; textSize = 18f
            setTextColor(Color.parseColor("#E8EAF0")); gravity = Gravity.CENTER
            setPadding(0, 0, 0, 10)
        })

        root.addView(c("CONTROLS · sensibilidade dos analógicos", "#9AA0AE"))
        root.addView(c("Deadzone"))
        val dzLabel = c("—", "#4FC3F7")
        root.addView(dzLabel)
        root.addView(seekBar(90, (Prefs.deadzone * 100).toInt()) {
            Prefs.deadzone = it / 100f
            dzLabel.text = "%.2f".format(Prefs.deadzone)
        })
        root.addView(c("Sensibilidade"))
        val sensLabel = c("—", "#4FC3F7")
        root.addView(sensLabel)
        root.addView(seekBar(300, (Prefs.sensitivity * 100).toInt()) {
            Prefs.sensitivity = it / 100f
            sensLabel.text = "%.2f".format(Prefs.sensitivity)
        })
        root.addView(c("Intensidade máxima"))
        val maxLabel = c("—", "#4FC3F7")
        root.addView(maxLabel)
        root.addView(seekBar(100, (Prefs.maxIntensity * 100).toInt()) {
            Prefs.maxIntensity = (it.coerceAtLeast(10)) / 100f
            maxLabel.text = "%.2f".format(Prefs.maxIntensity)
        })
        root.addView(CheckBox(this).apply {
            text = "Inverter Y (stick)"; setTextColor(Color.parseColor("#E8EAF0"))
            isChecked = Prefs.invertY
            setOnCheckedChangeListener { _, v -> Prefs.invertY = v }
        })

        root.addView(c("INTERFACE", "#9AA0AE"))
        root.addView(c("Escala dos controles"))
        root.addView(seekBar(100, ((Prefs.controllerSize - 0.5f) * 100).toInt()) {
            Prefs.controllerSize = 0.5f + it / 100f
        })
        root.addView(c("Opacidade"))
        root.addView(seekBar(100, (Prefs.opacity * 100).toInt()) {
            Prefs.opacity = (it.coerceAtLeast(35)) / 100f
        })
        root.addView(c("Espaçamento (px) entre os Joy-Cons — 0 = centralizados"))
        root.addView(seekBar(400, Prefs.spacing.toInt()) {
            Prefs.spacing = it.toFloat()
        })

        root.addView(c("DESEMPENHO", "#9AA0AE"))
        root.addView(c("Envio de eventos (Hz)"))
        val hzLabel = c("—", "#4FC3F7")
        root.addView(hzLabel)
        root.addView(seekBar(240, Prefs.sendHz) {
            Prefs.sendHz = it.coerceIn(30, 240)
            hzLabel.text = "${Prefs.sendHz} Hz"
        })

        root.addView(c("ANDROID · modo de entrada", "#9AA0AE"))
        val grp = RadioGroup(this)
        fun radio(t: String, mode: Int): RadioButton = RadioButton(this).apply {
            text = t
            setTextColor(Color.parseColor("#E8EAF0"))
            isChecked = Prefs.injectMode == mode
            setOnClickListener { Prefs.injectMode = mode }
        }
        grp.addView(radio("Somente teste in-app (padrão — não afeta jogos)", 1))
        grp.addView(radio("Toque via Acessibilidade (mapeia botões → gestos)", 2))
        root.addView(grp)
        root.addView(c("O modo GAMEPAD GLOBAL não é uma escolha do app: ele fica automático assim que o " +
            "daemon é iniciado pelo PC (USB/ADB). Ele tem prioridade porque injeta via InputManager do shell.", "#667085"))

        root.addView(Button(this).apply {
            text = "ABRIR CONFIGURAÇÕES DE ACESSIBILIDADE"
            setTextColor(Color.parseColor("#E8EAF0"))
            setBackgroundColor(Color.parseColor("#2C303A"))
            setOnClickListener {
                try { startActivity(Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS)) } catch (e: Exception) { }
            }
        }, LinearLayout.LayoutParams(-1, 130).apply { topMargin = 12 })

        root.addView(c("Mapeamento para o modo toque (nome=x,y frações da tela; use LSTICK/RSTICK também):", "#667085"))
        val mapBox = EditText(this).apply {
            setText(Prefs.a11yMapping)
            setTextColor(Color.parseColor("#4FC3F7"))
            setBackgroundColor(Color.parseColor("#24272E"))
            textSize = 12f
            setSingleLine(false)
        }
        root.addView(mapBox)
        root.addView(Button(this).apply {
            text = "SALVAR MAPEAMENTO"
            setTextColor(Color.parseColor("#E8EAF0"))
            setBackgroundColor(Color.parseColor("#2C303A"))
            setOnClickListener {
                Prefs.a11yMapping = mapBox.text.toString().trim()
                android.widget.Toast.makeText(this@SettingsActivity, "salvo", android.widget.Toast.LENGTH_SHORT).show()
            }
        }, LinearLayout.LayoutParams(-1, 110).apply { topMargin = 8 })

        return ScrollView(this).apply {
            addView(root)
            setBackgroundColor(Color.parseColor("#1B1D22"))
        }
    }
}
