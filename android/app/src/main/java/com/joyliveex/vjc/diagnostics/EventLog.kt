package com.joyliveex.vjc.diagnostics

import android.util.Log
import java.text.SimpleDateFormat
import java.util.ArrayDeque
import java.util.Date
import java.util.Locale
import java.util.concurrent.CopyOnWriteArrayList

/** Ring buffer + live listeners feeding the Diagnostics screen (mirrors PC Logger). */
object EventLog {
    private const val TAG = "VJC"
    private const val CAP = 600
    private val buf = ArrayDeque<String>()
    val listeners = CopyOnWriteArrayList<() -> Unit>()
    private val fmt = SimpleDateFormat("HH:mm:ss.SSS", Locale.US)

    @Synchronized
    fun add(source: String, msg: String, toLogcat: Boolean = true) {
        val line = "[${fmt.format(Date())}] $source: $msg"
        buf.addLast(line)
        while (buf.size > CAP) buf.removeFirst()
        if (toLogcat) Log.i(TAG, "$source: $msg")
        listeners.forEach { try { it() } catch (e: Exception) { } }
    }

    @Synchronized
    fun snapshot(): String = buf.joinToString("\n")
}
