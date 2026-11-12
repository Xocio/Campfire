pluginManagement {
    repositories {
        google {
            content {
                includeGroupByRegex("com\\.android.*")
                includeGroupByRegex("com\\.google.*")
                includeGroupByRegex("androidx.*")
            }
        }
        mavenCentral()
        gradlePluginPortal()
    }
}

plugins {
    id("org.gradle.toolchains.foojay-resolver-convention") version "1.0.0"
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
    }
}

rootProject.name = "Campfire"

// ZeroUI is a clone of https://github.com/Xocio/ZeroUI at the commit in zeroui-commit.txt.
if (!file("zeroui/settings.gradle.kts").isFile) {
    val commit = file("zeroui-commit.txt").readText().trim()
    throw GradleException(
        "ZeroUI is missing. Run: git clone https://github.com/Xocio/ZeroUI.git zeroui && git -C zeroui checkout $commit",
    )
}

includeBuild("zeroui") {
    dependencySubstitution {
        substitute(module("com.zeroui:zeroui-kit")).using(project(":kit"))
    }
}

include(":core")
include(":easytier")
include(":app")
include(":fakegame")
include(":baselineprofile")
