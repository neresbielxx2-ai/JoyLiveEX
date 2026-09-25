import java.util.Properties

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "com.joyliveex.vjc"
    compileSdk = 34

    defaultConfig {
        applicationId = "com.joyliveex.vjc"
        minSdk = 26
        targetSdk = 34
        versionCode = 1
        versionName = "1.0"
        resConfigs("en")
    }

    // Release signing: reads android/keystore.properties when present (CI secret or local).
    // Without it, release builds are signed with the debug key so the APK stays installable —
    // never ship that to users; see docs/BUILD.md.
    val ksProps = rootProject.file("keystore.properties")
    signingConfigs {
        if (ksProps.exists()) {
            create("vjc") {
                val p = Properties().apply { ksProps.inputStream().use { load(it) } }
                storeFile = rootProject.file(p.getProperty("storeFile"))
                storePassword = p.getProperty("storePassword")
                keyAlias = p.getProperty("keyAlias")
                keyPassword = p.getProperty("keyPassword")
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            signingConfig = if (ksProps.exists())
                signingConfigs.getByName("vjc")
            else
                signingConfigs.getByName("debug")
        }
        debug {
            isMinifyEnabled = false
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = "17"
    }
    lint {
        abortOnError = false
    }
    testOptions {
        unitTests.isReturnDefaultValues = true
    }
}

dependencies {
    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.appcompat:appcompat:1.6.1")
    implementation("com.google.android.material:material:1.11.0")
    implementation("androidx.constraintlayout:constraintlayout:2.1.4")
    implementation("androidx.lifecycle:lifecycle-service:2.7.0")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.8.1")
    testImplementation("junit:junit:4.13.2")
}
