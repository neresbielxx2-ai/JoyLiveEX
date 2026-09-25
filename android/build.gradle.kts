// Root build file — modules:
//   :app    Android companion (Kotlin, Views-based UI)
//   :daemon pure-Java input daemon, dexed with d8, run on-device via `adb shell app_process`
plugins {
    id("com.android.application") version "8.4.0" apply false
    id("org.jetbrains.kotlin.android") version "1.9.24" apply false
}
