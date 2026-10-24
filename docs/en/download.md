# Download

<script setup>
import rel from '../.vitepress/release.json'
const GH = 'https://github.com/Xocio/Campfire/releases'
const app = rel.app || {}
const mirror = rel.mirror && app.file ? rel.mirror.replace(/\/?$/, '/') + app.file : ''
const size = app.size ? (app.size / 1024 / 1024).toFixed(1) + ' MB' : ''
</script>

<DownloadCard inline />

## File

<table class="dl-table">
  <thead>
    <tr><th>File</th><th>Version</th><th>Size</th><th>Download</th></tr>
  </thead>
  <tbody>
    <tr>
      <td><code>Campfire.exe</code></td>
      <td>{{ rel.version || '—' }}</td>
      <td>{{ size || '—' }}</td>
      <td><template v-if="mirror"><a :href="mirror">Mirror</a> · </template><a :href="GH" target="_blank" rel="noopener">GitHub</a></td>
    </tr>
  </tbody>
</table>

<div v-if="app.sha256" class="dl-sum">

**SHA-256**

<code>{{ app.sha256 }}</code>

</div>

Both channels serve the same digitally signed file. Download Campfire only from this site or GitHub Releases, never from unknown redistributions. To verify the file, open a terminal in its folder and run

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
| Disk space | About 1.3 MB for the program, plus about 14 MB for the networking component on first use |

On Windows 7 and 8, download .NET Framework 4.8 from [Microsoft](https://dotnet.microsoft.com/download/dotnet-framework/net48) if it is not installed yet.

## Installing and updating

Campfire is a single executable that runs without installation; keep it in any folder you like. Your data lives in `%LocalAppData%\Campfire`, independent of where the program is, so moving or replacing the executable keeps all settings.

Official releases check for updates at startup. When a new version is found, Campfire asks first, then downloads the update, verifies its digital signature and restarts with the new version. If the program folder is not writable, it opens this page for a manual download instead.

See [Getting Started](/en/guide/getting-started) for the first steps.

## Download problems

- **The mirror is slow or interrupted**: retry on another network or use GitHub instead.
- **The browser or Windows warns about an uncommon file**: new releases have few downloads yet and may trigger reputation checks such as SmartScreen. Once the SHA-256 matches, choose Keep or Run anyway.
- **An antivirus blocks the file**: tools that modify other programs are occasionally flagged by mistake. Campfire is fully open source; review the code or [build it yourself](https://github.com/Xocio/Campfire/blob/main/README.en.md#building-from-source).
- **It does not start**: make sure .NET Framework 4.8 is installed, and see the [FAQ](/en/guide/faq).
