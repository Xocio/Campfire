# Download

<script setup>
import rel from '../.vitepress/release.json'
const GH = 'https://github.com/Xocio/Campfire/releases'
const app = rel.app || {}
const mirror = app.drive || ''
const android = rel.android || {}
const androidMirror = android.drive || ''
const androidGithub = android.tag ? GH + '/tag/' + android.tag : GH
</script>

::: tip Cloud drive
Because of the large number of users, the direct download link in China has exceeded its Free plan, so downloads in China are now served from a **cloud drive** that needs no proxy. Files there are named by version and are the same digitally signed files as on GitHub Releases; check them against the SHA-256 below.
:::

<DownloadRow />

## File

<table class="dl-table">
  <thead>
    <tr><th>File</th><th>Version</th><th>Download</th></tr>
  </thead>
  <tbody>
    <tr>
      <td><code>Campfire.exe</code></td>
      <td>{{ rel.version || '—' }}</td>
      <td><template v-if="mirror"><a :href="mirror" target="_blank" rel="noopener">Cloud drive</a> · </template><a :href="GH" target="_blank" rel="noopener">GitHub</a></td>
    </tr>
  </tbody>
</table>

<div v-if="app.sha256" class="dl-sum">

**SHA-256**

<code>{{ app.sha256 }}</code>

</div>

Both channels serve the same digitally signed file. Download Campfire only from this site, the cloud drive link it gives, or GitHub Releases, never from unknown redistributions. To verify the file, open a terminal in its folder and run

```powershell
certutil -hashfile Campfire.exe SHA256
```

The result must match the value above exactly.

## System requirements

| Item | Requirement |
| --- | --- |
| Operating system | Windows 7 SP1, 8, 8.1, 10, 11 |
| Runtime | .NET Framework 4.8, included in Windows 10 and 11 |
| Multiplayer | Windows 7 additionally needs updates KB3063858 and KB4474419 |
| Disk space | About 14 MB for the networking component, downloaded on first use |

On Windows 7 and 8, download .NET Framework 4.8 from [Microsoft](https://dotnet.microsoft.com/download/dotnet-framework/net48) if it is not installed yet.

## Installing and updating

Campfire is a single executable that runs without installation; keep it in any folder you like. Your data lives in `%LocalAppData%\Campfire`, independent of where the program is, so moving or replacing the executable keeps all settings.

Official releases check for updates at startup. When a new version is found, there are two ways to update: **Update** opens a cloud drive in the browser, and you replace the program file after downloading; **GitHub** lets Campfire download the update, verify its digital signature and restart with the new version. If the program folder is not writable, **GitHub** opens the GitHub download page instead.

See [Getting Started](/en/guide/getting-started) for the first steps.

## Android

<p v-if="!rel.android">Once the Android app is released, its download and checksum appear here.</p>

<template v-if="rel.android">

<table class="dl-table">
  <thead>
    <tr><th>File</th><th>Version</th><th>Download</th></tr>
  </thead>
  <tbody>
    <tr>
      <td><code>Campfire.apk</code></td>
      <td>{{ android.version }}</td>
      <td><template v-if="androidMirror"><a :href="androidMirror" target="_blank" rel="noopener">Cloud drive</a> · </template><a :href="androidGithub" target="_blank" rel="noopener">GitHub</a></td>
    </tr>
  </tbody>
</table>

<div v-if="android.sha256" class="dl-sum">

**SHA-256**

<code>{{ android.sha256 }}</code>

</div>

</template>

| Item | Requirement |
| --- | --- |
| System | Android 7.0 or later |
| Processor | 64-bit ARM |
| Game | An Android launcher that runs Minecraft: Java Edition |

The package is signed with Campfire's release certificate. On first install, Android may ask you to allow your browser or file manager to install apps from unknown sources. To update, install the new version over the old one; app data is kept. See [Android](/en/guide/android) for the full guide.

## Download problems

- **The cloud drive is slow or interrupted**: downloads may be throttled without signing in or a membership; sign in and retry, or use GitHub instead.
- **The browser or Windows warns about an uncommon file**: new releases have few downloads yet and may trigger reputation checks such as SmartScreen. Once the SHA-256 matches, choose Keep or Run anyway.
- **An antivirus blocks the file**: tools that modify other programs are occasionally flagged by mistake. Campfire is fully open source; review the code or [build it yourself](https://github.com/Xocio/Campfire/blob/main/README.en.md#building-from-source).
- **It does not start**: make sure .NET Framework 4.8 is installed, and see the [FAQ](/en/guide/faq).
