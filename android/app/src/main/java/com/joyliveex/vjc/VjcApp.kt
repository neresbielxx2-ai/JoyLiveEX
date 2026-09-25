package com.joyliveex.vjc

import android.app.Application
import com.joyliveex.vjc.config.Prefs

class VjcApp : Application() {
    override fun onCreate() {
        super.onCreate()
        Prefs.init(applicationContext)
    }
}
