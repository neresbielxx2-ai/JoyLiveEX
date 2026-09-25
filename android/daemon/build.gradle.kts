import java.util.Properties

plugins {
    id("java-library")
}

java {
    sourceCompatibility = JavaVersion.VERSION_11
    targetCompatibility = JavaVersion.VERSION_11
}

// The daemon runs inside the Android framework via `app_process` as the shell user,
// so it must be a DEX jar (like scrcpy's server). We compile plain javac output against
// no android.jar at all (100% reflection on the framework), jar it, dex it with d8 from
// the local Android SDK, then package classes.dex into vjc-daemon.jar.

fun androidHomeDir(): File? {
    System.getenv("ANDROID_HOME")?.let { File(it) }?.takeIf { it.isDirectory }?.let { return it }
    System.getenv("ANDROID_SDK_ROOT")?.let { File(it) }?.takeIf { it.isDirectory }?.let { return it }
    val local = rootProject.file("local.properties")
    if (local.exists()) {
        val props = Properties()
        local.inputStream().use { props.load(it) }
        val dir = props.getProperty("sdk.dir")?.let { File(it) }?.takeIf { f -> f.isDirectory }
        if (dir != null) return dir
    }
    return null
}

val mainSourceSet = sourceSets["main"]

val classesJar by tasks.registering(Jar::class) {
    group = "vjc"
    dependsOn(tasks.named("compileJava"))
    from(mainSourceSet.output)
    archiveFileName.set("vjc-daemon-classes.jar")
    destinationDirectory.set(layout.buildDirectory.dir("intermediate"))
}

val d8Out = layout.buildDirectory.dir("d8-out")

val d8Task by tasks.registering {
    group = "vjc"
    description = "Dexes the compiled daemon classes with the local Android SDK d8"
    dependsOn(classesJar)

    val sdk = androidHomeDir()
    val d8Jar = if (sdk != null) {
        File(sdk, "build-tools").listFiles()
            ?.filter { it.isDirectory && File(it, "lib/d8.jar").exists() }
            ?.sortedBy { it.name }
            ?.reversed()
            ?.firstOrNull()
            ?.let { File(it, "lib/d8.jar") }
    } else null
    val androidJar = if (sdk != null) {
        listOf("android-34", "android-35", "android-33")
            .map { File(sdk, "platforms/$it/android.jar") }
            .firstOrNull { it.exists() }
    } else null

    enabled = d8Jar != null
    inputs.files(classesJar)
    outputs.dir(d8Out)

    doLast {
        if (d8Jar == null) return@doLast
        val outDir = d8Out.get().asFile
        outDir.mkdirs()
        val input = classesJar.flatMap { it.archiveFile }.get().asFile
        val javaBin = File(System.getProperty("java.home"), "bin/java").absolutePath
        val cmd = mutableListOf(
            javaBin, "-cp", d8Jar.absolutePath, "com.android.tools.r8.D8",
            "--release", "--min-api", "26",
        )
        if (androidJar != null) { cmd += "--lib"; cmd += androidJar.absolutePath }
        cmd += "--output"; cmd += outDir.absolutePath; cmd += input.absolutePath
        project.exec { commandLine(cmd) }
    }
}

val dexJar by tasks.registering(Jar::class) {
    group = "vjc"
    description = "vjc-daemon.jar (classes.dex) — the on-device input daemon"
    dependsOn(d8Task)
    from(d8Out)
    archiveFileName.set("vjc-daemon.jar")
    destinationDirectory.set(layout.buildDirectory.dir("libs"))
    onlyIf { d8Out.get().asFile.resolve("classes.dex").exists() }
}

// Without the Android SDK locally (rare), `assemble` still works; CI builds dexJar.
tasks.named("build") {
    dependsOn(dexJar)
}
