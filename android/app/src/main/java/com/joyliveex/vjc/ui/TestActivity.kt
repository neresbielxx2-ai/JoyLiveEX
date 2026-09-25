package com.joyliveex.vjc.ui

import android.app.Activity
import android.graphics.Color
import android.os.Bundle
import android.view.Gravity
import android.view.KeyEvent
import android.view.View
import android.view.ViewGroup
import android.view.WindowManager
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import android.view.MotionEvent
import android.view.InputDevice
import com.joyliveex.vjc.config.Prefs
import com.joyliveex.vjc.diagnostics.EventLog

/**
 * CONTROLLER TEST (spec §15). This screen listens to real Android InputEvents.
 *
 * How to read it:
 *  - With the PC daemon running (USB), injected gamepad events arrive HERE as if
 *    a physical pad were connected: SOURCE_GAMEPAD + AXIS_X/Y/Z/RZ + BUTTON_*.
 *    Any game gets the same stream while focused — proving global delivery.
 *  - In-app/accessibility modes obviously won't light this screen from another
 *    process; that's what the mode is, no smoke and mirrors.
 */
class TestActivity : Activity() {

    private val rows = HashMap<Int, TextView>()
    private val axisRows = HashMap<String, TextView>()
    private lateinit var sourceText: TextView
    private lateinit var deviceText: TextView
    private lateinit var logText: TextView
    private var lastDeviceId = -999
    private var lastSource = 0

    private val buttonDefs = listOf(
        "A" to KeyEvent.KEYCODE_BUTTON_A,
        "B" to KeyEvent.KEYCODE_BUTTON_B,
        "X" to KeyEvent.KEYCODE_BUTTON_X,
        "Y" to KeyEvent.KEYCODE_BUTTON_Y,
        "L1/L" to KeyEvent.KEYCODE_BUTTON_L1,
        "R1/R" to KeyEvent.KEYCODE_BUTTON_R1,
        "ZL/L2" to KeyEvent.KEYCODE_BUTTON_L2,
        "ZR/R2" to KeyEvent.KEYCODE_BUTTON_R2,
        "START/+" to KeyEvent.KEYCODE_BUTTON_START,
        "SELECT/−" to KeyEvent.KEYCODE_BUTTON_SELECT,
        "THUMBL" to KeyEvent.KEYCODE_BUTTON_THUMBL,
        "THUMBR" to KeyEvent.KEYCODE_BUTTON_THUMBR,
        "UP" to KeyEvent.KEYCODE_DPAD_UP,
        "DOWN" to KeyEvent.KEYCODE_DPAD_DOWN,
        "LEFT" to KeyEvent.KEYCODE_DPAD_LEFT,
        "RIGHT" to KeyEvent.KEYCODE_DPAD_RIGHT,
    )

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        Prefs.init(applicationContext)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)

        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(Color.parseColor("#1B1D22"))
            setPadding(32, 32, 32, 32)
        }
        root.addView(TextView(this).apply {
            text = "CONTROLLER TEST"
            textSize = 20f
            setTextColor(Color.parseColor("#E8EAF0"))
            gravity = Gravity.CENTER
            setPadding(0, 0, 0, 14)
        })

        val grid = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        fun col(defs: List<Pair<String, Int>>, axes: List<String>) {
            val c = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
            for ((name, code) in defs) {
                val t = TextView(this).apply {
                    text = "$name  ○"
                    textSize = 15f
                    setTextColor(Color.parseColor("#9AA0AE"))
                    setPadding(0, 6, 0, 6)
                }
                rows[code] = t
                c.addView(t)
            }
            for (ax in axes) {
                val t = TextView(this).apply {
                    text = "$ax: 0.00"
                    textSize = 13f
                    setTextColor(Color.parseColor("#4FC3F7"))
                    setPadding(0, 6, 0, 6)
                }
                axisRows[ax] = t
                c.addView(t)
            }
            grid.addView(c, LinearLayout.LayoutParams(0, -2, 1f))
        }
        col(buttonDefs.subList(0, 8), listOf("AXIS_X", "AXIS_Y", "AXIS_LTRIGGER", "AXIS_HAT_X"))
        col(buttonDefs.subList(8, 16), listOf("AXIS_Z", "AXIS_RZ", "AXIS_RTRIGGER", "AXIS_HAT_Y"))
        root.addView(grid)

        sourceText = TextView(this).apply {
            text = "SOURCE: —"
            textSize = 13f
            setTextColor(Color.parseColor("#66D277"))
            setPadding(0, 16, 0, 0)
        }
        deviceText = TextView(this).apply {
            text = "DEVICE: —"
            textSize = 13f
            setTextColor(Color.parseColor("#66D277"))
            setPadding(0, 4, 0, 0)
        }
        root.addView(sourceText)
        root.addView(deviceText)

        logText = TextView(this).apply {
            text = "evento mais recente: —"
            textSize = 11f
            setTextColor(Color.parseColor("#9AA0AE"))
            setPadding(0, 10, 0, 10)
        }
        root.addView(logText)

        root.addView(TextView(this).apply {
            text = "Teste real: deixe esta tela aberta e pressione controles no PC — os eventos injetados " +
                "chegam aqui como InputEvents de gamepad (KEYCODE_BUTTON_*, AXIS_*). " +
                "Feche esta tela e abra um jogo: os mesmos eventos vão para o jogo."
            textSize = 11f
            setTextColor(Color.parseColor("#667085"))
            setPadding(0, 8, 0, 16)
        })

        setContentView(ScrollView(this).apply {
            addView(root)
            setBackgroundColor(Color.parseColor("#1B1D22"))
        })
    }

    override fun onKeyDown(keyCode: Int, event: KeyEvent): Boolean {
        markKey(keyCode, true, event)
        return true
    }

    override fun onKeyUp(keyCode: Int, event: KeyEvent): Boolean {
        markKey(keyCode, false, event)
        return true
    }

    private fun markKey(keyCode: Int, down: Boolean, event: KeyEvent) {
        rows[keyCode]?.let { t ->
            val name = buttonDefs.first { it.second == keyCode }.first
            t.text = "$name  ${if (down) "●" else "○"}"
            t.setTextColor(if (down) Color.parseColor("#4FC3F7") else Color.parseColor("#9AA0AE"))
        }
        updateMeta(event.deviceId, event.source)
        logText.text = "evento: ${if (down) "DOWN" else "UP  "} KEYCODE $keyCode (device ${event.deviceId})"
        if (down) EventLog.add("TEST", "keydown $keyCode src=0x${Integer.toHexString(event.source)}", false)
    }

    override fun onGenericMotionEvent(event: MotionEvent): Boolean {
        if (event.source and (InputDevice.SOURCE_GAMEPAD or InputDevice.SOURCE_JOYSTICK) == 0 &&
            event.action != MotionEvent.ACTION_MOVE
        ) return super.onGenericMotionEvent(event)

        fun fmt(axis: Int) = event.getAxisValue(axis)
        axisRows["AXIS_X"]?.text = "AXIS_X: %.2f".format(fmt(MotionEvent.AXIS_X))
        axisRows["AXIS_Y"]?.text = "AXIS_Y: %.2f".format(fmt(MotionEvent.AXIS_Y))
        axisRows["AXIS_Z"]?.text = "AXIS_Z: %.2f".format(fmt(MotionEvent.AXIS_Z))
        axisRows["AXIS_RZ"]?.text = "AXIS_RZ: %.2f".format(fmt(MotionEvent.AXIS_RZ))
        axisRows["AXIS_LTRIGGER"]?.text = "AXIS_LTRIGGER: %.2f".format(fmt(MotionEvent.AXIS_LTRIGGER))
        axisRows["AXIS_RTRIGGER"]?.text = "AXIS_RTRIGGER: %.2f".format(fmt(MotionEvent.AXIS_RTRIGGER))
        axisRows["AXIS_HAT_X"]?.text = "AXIS_HAT_X: %.2f".format(fmt(MotionEvent.AXIS_HAT_X))
        axisRows["AXIS_HAT_Y"]?.text = "AXIS_HAT_Y: %.2f".format(fmt(MotionEvent.AXIS_HAT_Y))

        updateMeta(event.deviceId, event.source)
        logText.text = "motion: device ${event.deviceId} src=0x${Integer.toHexString(event.source)} " +
            "buttons=0x${Integer.toHexString(event.buttonState)}"
        return true
    }

    private fun updateMeta(deviceId: Int, source: Int) {
        if (deviceId == lastDeviceId && source == lastSource) return
        lastDeviceId = deviceId
        lastSource = source
        val gamepad = source and InputDevice.SOURCE_GAMEPAD != 0
        val joystick = source and InputDevice.SOURCE_JOYSTICK != 0
        val kind = when {
            gamepad -> "GAMEPAD"
            joystick -> "JOYSTICK"
            else -> "OUTRO"
        }
        sourceText.text = "SOURCE: $kind (0x${Integer.toHexString(source)})"
        val name = try { InputDevice.getDevice(deviceId)?.name } catch (e: Exception) { null }
        deviceText.text = "DEVICE: ${name ?: "Virtual Controller (injetado, id=$deviceId)"}"
    }
}
