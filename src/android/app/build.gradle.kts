import java.util.Properties

plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.compose)
    alias(libs.plugins.kotlin.serialization)
    alias(libs.plugins.aboutlibraries)
    alias(libs.plugins.androidx.baselineprofile)
}

android {
    namespace = "top.mccampfire.app"
    compileSdk = libs.versions.compileSdk.get().toInt()

    defaultConfig {
        applicationId = "top.mccampfire.app"
        minSdk = libs.versions.minSdk.get().toInt()
        targetSdk = libs.versions.targetSdk.get().toInt()
        versionCode = 1
        versionName = "0.1.0"

        val localText = providers.fileContents(rootProject.layout.projectDirectory.file("local.properties")).asText.orNull.orEmpty()
        val localProps = Properties().apply { load(localText.reader()) }
        val serviceUrl = (providers.gradleProperty("campfire.serviceUrl").orNull ?: localProps.getProperty("campfire.serviceUrl")).orEmpty().trim()
        require(!serviceUrl.contains('"') && !serviceUrl.contains('\\')) { "campfire.serviceUrl must be a plain URL" }
        buildConfigField("String", "SERVICE_URL", "\"$serviceUrl\"")
    }
    buildTypes {
        release {
            ndk {
                abiFilters += "arm64-v8a"
            }
            optimization {
                enable = true
            }
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro",
            )
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_21
        targetCompatibility = JavaVersion.VERSION_21
    }
    buildFeatures {
        compose = true
        buildConfig = true
    }
    androidResources {
        localeFilters += listOf("en", "zh", "yue")
    }
    testOptions {
        unitTests.isReturnDefaultValues = true
    }
}

androidComponents {
    finalizeDsl { extension ->
        extension.buildTypes
            .filter { it.name.startsWith("nonMinified") }
            .forEach { it.optimization.enable = false }
    }
}

dependencies {
    implementation("com.zeroui:zeroui-kit")
    implementation(project(":core"))
    implementation(project(":easytier"))

    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.activity.compose)
    implementation(libs.androidx.lifecycle.runtime.compose)
    implementation(libs.androidx.lifecycle.viewmodel.compose)
    implementation(libs.compose.ui.tooling.preview)
    implementation(libs.kotlinx.serialization.json)
    implementation(libs.kyant.backdrop)
    implementation(libs.kyant.shapes)
    implementation(libs.miuix.blur)
    implementation(libs.materialkolor.utilities)
    implementation(libs.aboutlibraries.core)
    implementation(libs.androidx.profileinstaller)
    baselineProfile(project(":baselineprofile"))

    debugImplementation(libs.compose.ui.tooling)

    testImplementation(libs.junit)
    testImplementation(libs.kotlinx.coroutines.test)
}

composeCompiler {
    stabilityConfigurationFiles.add(layout.projectDirectory.file("stability_config.conf"))
    if (providers.gradleProperty("zeroui.composeReports").isPresent) {
        reportsDestination.set(layout.buildDirectory.dir("compose_reports"))
        metricsDestination.set(layout.buildDirectory.dir("compose_metrics"))
    }
}
