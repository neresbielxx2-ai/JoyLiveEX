package com.joyliveex.vjc.ui

import android.app.Activity
import android.content.Intent
import android.graphics.Color
import android.text.InputType
import android.os.Bundle
import android.view.Gravity
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.RadioButton
import android.widget.RadioGroup
import android.widget.ScrollView
import android.widget.TextView
import android.widget.Toast
import com.joyliveex.vjc.ControllerService
import com.joyliveex.vjc.config.Prefs
import com.joyliveex.vjc.diagnostics.EventLog
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress
import java.nio.charset.StandardCharsets

/**
 * Pairing screen: LAN (with automatic broadcast discovery of the PC), Internet
 * (pairing code + optional relay) and USB (adb reverse — the PC drives this mode).
 */
class PairActivity : Activity() {

    private lateinit var lanBtn: RadioButton
    private lateinit var netBtn: RadioButton
    private lateinit var usbBtn: RadioButton
    private lateinit var hostBox: EditText
    private lateinit var portBox: EditText
    private lateinit var codeBox: EditText
    private lateinit var relayHostBox: EditText
    private lateinit var foundList: TextView

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        Prefs.init(applicationContext)
        buildUi()
    }

    private fun label(t: String) = TextView(this).apply {
        text = t
        setTextColor(Color.parseColor("#9AA0AE"))
        textSize = 12f
        setPadding(0, 18, 0, 4)
    }

    private fun buildUi() {
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(Color.parseColor("#1B1D22"))
            setPadding(36, 36, 36, 36)
        }
        root.addView(TextView(this).apply {
            text = "PAREAMENTO"
            textSize = 20f
            setTextColor(Color.parseColor("#E8EAF0"))
            gravity = Gravity.CENTER
            setPadding(0, 0, 0, 16)
        })

        val group = RadioGroup(this).apply { orientation = LinearLayout.HORIZONTAL }
        lanBtn = RadioButton(this).apply { text = "LAN"; setTextColor(Color.parseColor("#E8EAF0")) }
        netBtn = RadioButton(this).apply { text = "INTERNET"; setTextColor(Color.parseColor("#E8EAF0")) }
        usbBtn = RadioButton(this).apply { text = "USB (ADB)"; setTextColor(Color.parseColor("#E8EAF0")) }
        group.addView(lanBtn); group.addView(netBtn); group.addView(usbBtn)
        when (Prefs.mode) {
            Prefs.MODE_INTERNET -> netBtn.isChecked = true
            Prefs.MODE_USB -> usbBtn.isChecked = true
            else -> lanBtn.isChecked = true
        }
        root.addView(group)

        hostBox = EditText(this).apply {
            hint = "IP do PC (ex.: 192.168.0.42)"; setTextColor(Color.parseColor("#E8EAF0"))
            setBackgroundColor(Color.parseColor("#24272E"))
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_URI
            setText(Prefs.host)
        }
        portBox = EditText(this).apply {
            hint = "porta"; setTextColor(Color.parseColor("#E8EAF0"))
            setBackgroundColor(Color.parseColor("#24272E"))
            inputType = InputType.TYPE_CLASS_NUMBER
            setText(Prefs.pcPort.toString())
        }
        codeBox = EditText(this).apply {
            hint = "CÓDIGO DA SESSÃO (ex.: 8F4K-72LM)"; setTextColor(Color.parseColor("#4FC3F7"))
            setBackgroundColor(Color.parseColor("#24272E")); textSize = 22f; letterSpacing = 0.18f
            setText(Prefs.code)
        }
        relayHostBox = EditText(this).apply {
            hint = "host do relay (opcional, ex.: relay.meuhost.com)"; setTextColor(Color.parseColor("#E8EAF0"))
            setBackgroundColor(Color.parseColor("#24272E"))
            setText(Prefs.relayHost)
        }

        root.addView(label("Endereço do PC"))
        root.addView(hostBox)
        root.addView(label("Porta (UDP/TCP do PC)"))
        root.addView(portBox)
        root.addView(label("Host do relay (apenas INTERNET)"))
        root.addView(relayHostBox)
        root.addView(label("Código da sessão mostrado no EXE"))
        root.addView(codeBox)

        val actions = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; setPadding(0, 18, 0, 0) }
        actions.addView(Button(this).apply {
            text = "BUSCAR LAN"
            setTextColor(Color.parseColor("#E8EAF0"))
            setBackgroundColor(Color.parseColor("#2C303A"))
            setOnClickListener { discover() }
        }, LinearLayout.LayoutParams(0, 120, 1f).apply { marginEnd = 8 })
        actions.addView(Button(this).apply {
            text = "CONECTAR"
            setTextColor(Color.parseColor("#E8EAF0"))
            setBackgroundColor(Color.parseColor("#2C303A"))
            setOnClickListener { connect() }
        }, LinearLayout.LayoutParams(0, 120, 1f))
        root.addView(actions)

        foundList = TextView(this).apply {
            text = "dispositivos: —"
            setTextColor(Color.parseColor("#9AA0AE"))
            setPadding(0, 18, 0, 0)
            textSize = 12f
        }
        root.addView(foundList)

        root.addView(TextView(this).apply {
            text = "USB: conecte o cabo e autorize a depuração — o EXE configura tudo via adb reverse e este app " +
                "diala 127.0.0.1 automaticamente. Nenhuma configuração de rede é necessária nesse modo."
            setTextColor(Color.parseColor("#667085"))
            textSize = 11f
            setPadding(0, 22, 0, 0)
        })

        setContentView(ScrollView(this).apply {
            addView(root)
            setBackgroundColor(Color.parseColor("#1B1D22"))
        })
    }

    private fun discover() {
        Thread {
            val results = ArrayList<String>()
            try {
                val sock = DatagramSocket().apply { broadcast = true; soTimeout = 700 }
                val probe = "VJCDprobe!".toByteArray(StandardCharsets.US_ASCII)
                val port = Prefs.pcPort
                for (ip in broadcastTargets()) {
                    sock.send(DatagramPacket(probe, probe.size, ip, port))
                }
                val deadline = System.currentTimeMillis() + 1600
                val buf = ByteArray(512)
                while (System.currentTimeMillis() < deadline) {
                    try {
                        val p = DatagramPacket(buf, buf.size)
                        sock.receive(p)
                        val s = String(p.data, 0, p.length, StandardCharsets.US_ASCII)
                        if (s.startsWith("VJCDrepl!")) {
                            val tcpPort = ((s[10].code and 0xFF) shl 8) or (s[11].code and 0xFF)
                            val clen = s[12].code and 0xFF
                            val code = s.substring(13, minOf(13 + clen, s.length))
                            val nice = if (code.length == 8) "${code.take(4)}-${code.drop(4)}" else code
                            results.add("${p.address.hostAddress}:$tcpPort · código $nice")
                        }
                    } catch (e: java.net.SocketTimeoutException) { /* keep listening until deadline */ }
                }
                sock.close()
            } catch (e: Exception) {
                EventLog.add("PAIR", "busca lan falhou: ${e.message}")
            }
            runOnUiThread {
                foundList.text = if (results.isEmpty()) "dispositivos: nenhum (o Virtual Joy-Con no PC precisa estar com LAN ativa)"
                else results.joinToString("\n") { "→ $it" }
                results.firstOrNull()?.let {
                    hostBox.setText(it.substringAfter("→ ").substringBefore(":"))
                }
            }
        }.start()
    }

    private fun broadcastTargets(): List<InetAddress> {
        val out = ArrayList<InetAddress>()
        out.add(InetAddress.getByAddress(byteArrayOf(-1, -1, -1, -1))) // 255.255.255.255
        try {
            val ifaces = java.net.NetworkInterface.getNetworkInterfaces()
            while (ifaces.hasMoreElements()) {
                val i = ifaces.nextElement()
                if (!i.isUp || i.isLoopback) continue
                for (addr in i.interfaceAddresses) {
                    val b = addr.broadcast ?: continue
                    out.add(b.address)
                }
            }
        } catch (e: Exception) { }
        return out
    }

    private fun connect() {
        val mode = when {
            usbBtn.isChecked -> Prefs.MODE_USB
            netBtn.isChecked -> Prefs.MODE_INTERNET
            else -> Prefs.MODE_LAN
        }
        Prefs.mode = mode
        Prefs.host = hostBox.text.toString().trim()
        Prefs.pcPort = portBox.text.toString().toIntOrNull()?.coerceIn(1024, 65535) ?: 8721
        Prefs.code = codeBox.text.toString().trim().uppercase()
        Prefs.relayHost = relayHostBox.text.toString().trim()
        if (mode == Prefs.MODE_LAN && Prefs.host.isEmpty()) {
            Toast.makeText(this, "digite o IP do PC (ou use BUSCAR LAN)", Toast.LENGTH_LONG).show()
            return
        }
        if (mode != Prefs.MODE_USB && Prefs.code.length < 7) {
            Toast.makeText(this, "código da sessão inválido — copie exatamente o mostrado no EXE", Toast.LENGTH_LONG).show()
            return
        }
        val i = Intent(this, ControllerService::class.java)
        i.putExtra(ControllerService.EXTRA, "start")
        try { startForegroundService(i) } catch (e: Exception) { Toast.makeText(this, "erro: ${e.message}", Toast.LENGTH_LONG).show() }
        Toast.makeText(this, "serviço iniciado — conectando…", Toast.LENGTH_LONG).show()
        finish()
    }
}
