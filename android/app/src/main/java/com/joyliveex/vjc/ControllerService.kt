package com.joyliveex.vjc

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import androidx.core.app.NotificationCompat
import com.joyliveex.vjc.config.Prefs
import com.joyliveex.vjc.controller.LocalInput
import com.joyliveex.vjc.diagnostics.EventLog
import com.joyliveex.vjc.input.InputRouter
import com.joyliveex.vjc.input.VjcAccessibilityService
import com.joyliveex.vjc.controller.ControllerState
import com.joyliveex.vjc.network.Transport
import com.joyliveex.vjc.network.TcpTransport
import com.joyliveex.vjc.network.UdpTransport
import com.joyliveex.vjc.network.VjcLink
import java.net.InetSocketAddress

/**
 * Foreground service = the "serviço de controle". Owns the connection to the PC
 * (USB/ADB via adb-reverse TCP, LAN UDP, Internet UDP through optional relay),
 * streams the phone's own touch state upstream and reacts to PC commands
 * (vibration, release-all, config push, daemon start/stop feedback).
 */
class ControllerService : Service() {

    companion object {
        @Volatile var running = false
        @Volatile var connected = false
        @Volatile var transportDesc = "—"
        @Volatile var rttMs = 0.0
        @Volatile var lossPct = 0.0
        @Volatile var isDirect = false
        @Volatile var lastError = ""
        var link: VjcLink? = null
        val listeners = java.util.concurrent.CopyOnWriteArrayList<() -> Unit>()

        fun notifyUi() = listeners.forEach { try { it() } catch (e: Exception) { } }

        const val ACTION = "com.joyliveex.vjc.CONTROL"
        const val EXTRA = "cmd" // "start" | "stop"
    }

    private var worker: Thread? = null

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val cmd = intent?.getStringExtra(EXTRA) ?: "start"
        if (cmd == "stop") {
            stopEverything()
            return START_NOT_STICKY
        }
        startForegroundCompat()
        Prefs.serviceShouldRun = true
        running = true
        notifyUi()
        if (worker?.isAlive != true) {
            worker = Thread(::connectionLoop, "vjc-client").apply { isDaemon = true; start() }
        }
        return START_STICKY
    }

    private fun startForegroundCompat() {
        val nm = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            nm.createNotificationChannel(
                NotificationChannel("vjc", "Virtual Joy-Con", NotificationManager.IMPORTANCE_LOW)
            )
        }
        val pi = PendingIntent.getActivity(
            this, 0, Intent(this, com.joyliveex.vjc.ui.MainActivity::class.java),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val n: Notification = NotificationCompat.Builder(this, "vjc")
            .setContentTitle("Virtual Joy-Con")
            .setContentText("Serviço de controle ativo")
            .setSmallIcon(R.mipmap.ic_stat_vjc)
            .setOngoing(true)
            .setContentIntent(pi)
            .build()
        if (Build.VERSION.SDK_INT >= 34)
            startForeground(1, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE)
        else
            startForeground(1, n)
    }

    private fun connectionLoop() {
        var backoff = 1000L
        while (running) {
            var transport: Transport? = null
            try {
                val code = Prefs.code
                val port = Prefs.pcPort
                val mode = Prefs.mode
                val t: Transport = when (mode) {
                    Prefs.MODE_USB -> {
                        transportDesc = "USB (adb reverse)"
                        TcpTransport("127.0.0.1", port)
                    }
                    Prefs.MODE_INTERNET -> {
                        transportDesc = "Internet (relay)"
                        val rhost = Prefs.relayHost
                        UdpTransport(InetSocketAddress(rhost, Prefs.relayPort), code.filter { it.isLetterOrDigit() }.padEnd(8, '0').take(8))
                    }
                    else -> {
                        transportDesc = "LAN local"
                        UdpTransport(InetSocketAddress(Prefs.host, port), null)
                    }
                }
                transport = t
                val l = VjcLink(t, code, object : VjcLink.Listener {
                    override fun onOpened() {
                        connected = true
                        backoff = 1000L
                        EventLog.add("NET", "conectado ao PC (${transportDesc})")
                        notifyUi()
                    }

                    override fun onClosed(reason: String) {
                        val wasConnected = connected
                        connected = false
                        notifyUi()
                        if (wasConnected) EventLog.add("NET", "conexão encerrada ($reason)")
                    }

                    override fun onStateFromPc(s: ControllerState) {
                        LocalInput.applyPcState(s)
                        InputRouter.onPcState(s)
                    }

                    override fun onCmd(cmd: Int, arg: ByteArray) {
                        when (cmd) {
                            com.joyliveex.vjc.network.Wire.CMD_VIBRATE -> {
                                if (arg.size >= 2 && Prefs.vibrationEnabled) {
                                    val ms = ((arg[0].toInt() and 0xFF) or ((arg[1].toInt() and 0xFF) shl 8))
                                    VjcAccessibilityService.instance?.vibrate(ms.coerceIn(10, 2000).toLong())
                                        ?: run {
                                            @Suppress("DEPRECATION")
                                            val vib = (getSystemService(Context.VIBRATOR_SERVICE) as? android.os.Vibrator)
                                            vib?.vibrate(ms.coerceIn(10, 2000).toLong())
                                        }
                                }
                            }
                            com.joyliveex.vjc.network.Wire.CMD_RELEASE_ALL -> LocalInput.releaseAll()
                            com.joyliveex.vjc.network.Wire.CMD_STOP_DAEMON -> running = false
                            com.joyliveex.vjc.network.Wire.CMD_CONFIG -> applyConfig(String(arg))
                            -1 -> LocalInput.releaseAll()
                        }
                        notifyUi()
                    }

                    override fun onLog(line: String) = EventLog.add("NET", line)
                })
                l.statusProvider = {
                    ControllerService.rttMs = l.rttMs
                    ControllerService.lossPct = l.lossPct
                    ControllerService.isDirect = (t as? UdpTransport)?.isDirect == true
                    notifyUi()
                    l.status(
                        Build.MODEL ?: "Android",
                        serviceState = 2,
                        inputMode = Prefs.injectMode,
                        appVer = appVersionName(),
                    )
                }
                link = l
                val sender = Thread { senderLoop(l) }
                sender.isDaemon = true
                sender.start()
                l.run()
                running = running && Prefs.serviceShouldRun
                if (!running) break
            } catch (e: Exception) {
                lastError = e.message ?: "erro"
                EventLog.add("NET", "falha: ${e.message}")
                if (!Prefs.autoReconnect) {
                    running = false
                    break
                }
            } finally {
                connected = false
                transport?.close()
                notifyUi()
            }
            if (running && Prefs.autoReconnect) {
                Thread.sleep(backoff.coerceAtMost(15000))
                backoff *= 2
            } else if (running) {
                Thread.sleep(1500) // retry dial without flooding while auto-reconnect is off
            }
        }
        connected = false
        running = false
        notifyUi()
        stopSelf()
    }

    private fun senderLoop(l: VjcLink) {
        val lastSent = ControllerState()
        var lastSendMs = 0L
        while (l.connected && running) {
            val now = System.currentTimeMillis()
            val state = LocalInput.drainIfNeeded(lastSent)
            if (state != null) {
                val minInterval = 1000L / Prefs.sendHz.coerceIn(10, 250)
                if (now - lastSendMs >= minInterval || !state.isIdle) {
                    // buttons changes are sent immediately; pure-axis motion is rate-capped
                    if (state.buttons != lastSent.buttons || !state.isIdle || now - lastSendMs >= minInterval) {
                        try { l.sendState(state) } catch (e: Exception) { return }
                        lastSendMs = now
                    }
                }
            } else if (now - lastSendMs > 500) {
                // idle safety: keep the daemon released state fresh at 2 Hz (anti-stuck)
                try { l.sendState(LocalInput.snapshot()) } catch (e: Exception) { return }
                lastSendMs = now
            }
            Thread.sleep(4)
        }
    }

    private fun applyConfig(json: String) {
        try {
            fun num(key: String): Float? =
                Regex("\"$key\"\\s*:\\s*(-?[0-9.]+)").find(json)?.groupValues?.get(1)?.toFloatOrNull()
            num("dz")?.let { Prefs.deadzone = it }
            num("sens")?.let { Prefs.sensitivity = it }
            num("invertY")?.let { Prefs.invertY = it > 0.5f }
            num("vib")?.let { Prefs.vibrationEnabled = it > 0.5f }
            EventLog.add("CFG", "configuração aplicada do PC (dz=${Prefs.deadzone})")
        } catch (e: Exception) { }
    }

    private fun appVersionName(): String = try {
        packageManager.getPackageInfo(packageName, 0).versionName ?: "1.0"
    } catch (e: Exception) {
        "1.0"
    }

    private fun stopEverything() {
        running = false
        Prefs.serviceShouldRun = false
        connected = false
        try { link?.close() } catch (e: Exception) { }
        notifyUi()
        stopForeground(STOP_FOREGROUND_REMOVE)
        stopSelf()
    }

    override fun onDestroy() {
        running = false
        connected = false
        super.onDestroy()
    }
}
