import java.security.MessageDigest

plugins {
    alias(libs.plugins.android.library)
}

android {
    namespace = "top.mccampfire.easytier"
    compileSdk = libs.versions.compileSdk.get().toInt()

    defaultConfig {
        minSdk = libs.versions.minSdk.get().toInt()
        consumerProguardFiles("consumer-rules.pro")
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_21
        targetCompatibility = JavaVersion.VERSION_21
    }
}

dependencies {
    implementation(project(":core"))
}

/** Fails the build when a prebuilt library differs from the hash in prebuilt.txt. */
val verifyEasyTierLibs by tasks.registering {
    val info = layout.projectDirectory.file("prebuilt.txt")
    val libs = layout.projectDirectory.dir("src/main/jniLibs")
    inputs.file(info)
    inputs.dir(libs)
    doLast {
        val expected = info.asFile.readLines()
            .filter { it.startsWith("sha256 ") }
            .associate { line -> line.split(' ').let { it[1] to it[2] } }
        val found = libs.asFile.walkTopDown().filter { it.isFile && it.name.endsWith(".so") }.toList()
        require(found.isNotEmpty()) { "No EasyTier libraries in ${libs.asFile}" }
        for (file in found) {
            val key = file.parentFile.name + "/" + file.name
            val digest = MessageDigest.getInstance("SHA-256").digest(file.readBytes()).joinToString("") { "%02x".format(it) }
            require(expected[key] == digest) { "$key does not match prebuilt.txt" }
        }
    }
}

tasks.named("preBuild") { dependsOn(verifyEasyTierLibs) }
