package com.joyliveex.vjc.config

import android.content.Context
import android.content.SharedPreferences

/** Persisted companion settings (mirrors a subset of the PC AppSettings). */
object Prefs {
    private lateinit var sp: SharedPreferences

    fun init(ctx: Context) {
        sp = ctx.getSharedPreferences("vjc", Context.MODE_PRIVATE)
    }

    var mode: Int
        get() = sp.getInt("mode", MODE_LAN)
        set(v) = sp.edit().putInt("mode", v).apply()

    var host: String
        get() = sp.getString("host", "") ?: ""
        set(v) = sp.edit().putString("host", v).apply()

    var pcPort: Int
        get() = sp.getInt("pcPort", 8721)
        set(v) = sp.edit().putInt("pcPort", v).apply()

    var code: String
        get() = sp.getString("code", "") ?: ""
        set(v) = sp.edit().putString("code", v).apply()

    var relayHost: String
        get() = sp.getString("relayHost", "") ?: ""
        set(v) = sp.edit().putString("relayHost", v).apply()

    var relayPort: Int
        get() = sp.getInt("relayPort", 8722)
        set(v) = sp.edit().putInt("relayPort", v).apply()

    var preferP2P: Boolean
        get() = sp.getBoolean("p2p", true)
        set(v) = sp.edit().putBoolean("p2p", v).apply()

    var autoReconnect: Boolean
        get() = sp.getBoolean("autoRe", true)
        set(v) = sp.edit().putBoolean("autoRe", v).apply()

    // controller feel (kept in sync with the EXE via CMD_CONFIG)
    var deadzone: Float
        get() = sp.getFloat("dz", 0.12f)
        set(v) = sp.edit().putFloat("dz", v).apply()

    var sensitivity: Float
        get() = sp.getFloat("sens", 1f)
        set(v) = sp.edit().putFloat("sens", v).apply()

    var invertY: Boolean
        get() = sp.getBoolean("invY", false)
        set(v) = sp.edit().putBoolean("invY", v).apply()

    var maxIntensity: Float
        get() = sp.getFloat("maxI", 1f)
        set(v) = sp.edit().putFloat("maxI", v).apply()

    var curve: Int
        get() = sp.getInt("curve", 0)
        set(v) = sp.edit().putInt("curve", v).apply()

    // behavior
    var injectMode: Int   // 0 = none, 1 = in-app echo, 2 = accessibility touch map
        get() = sp.getInt("inject", 1)
        set(v) = sp.edit().putInt("inject", v).apply()

    var vibrationEnabled: Boolean
        get() = sp.getBoolean("vib", true)
        set(v) = sp.edit().putBoolean("vib", v).apply()

    var vibrationMs: Int
        get() = sp.getInt("vibMs", 40)
        set(v) = sp.edit().putInt("vibMs", v).apply()

    /** key = button name (A,B,X,Y,UP,DOWN,...,LSTICK,RSTICK); value = "xPct,yPct" of tap target */
    var a11yMapping: String
        get() = sp.getString("a11yMap", defaultMapping) ?: defaultMapping
        set(v) = sp.edit().putString("a11yMap", v).apply()

    // interface
    var uiScale: Float
        get() = sp.getFloat("uiscale", 1f)
        set(v) = sp.edit().putFloat("uiscale", v).apply()

    var spacing: Float   // extra px between rails
        get() = sp.getFloat("spacing", 0f)
        set(v) = sp.edit().putFloat("spacing", v).apply()

    var opacity: Float
        get() = sp.getFloat("opacity", 0.94f)
        set(v) = sp.edit().putFloat("opacity", v).apply()

    var controllerSize: Float
        get() = sp.getFloat("csize", 1f)
        set(v) = sp.edit().putFloat("csize", v).apply()

    var sendHz: Int
        get() = sp.getInt("sendHz", 125)
        set(v) = sp.edit().putInt("sendHz", v).apply()

    var serviceShouldRun: Boolean
        get() = sp.getBoolean("svcRun", false)
        set(v) = sp.edit().putBoolean("svcRun", v).apply()

    private val defaultMapping = "A=0.72,0.52|B=0.80,0.60|X=0.64,0.60|Y=0.72,0.68"

    const val MODE_LAN = 0
    const val MODE_INTERNET = 1
    const val MODE_USB = 2
}
