# 下载

<script setup>
import rel from './.vitepress/release.json'
const GH = 'https://github.com/Xocio/Campfire/releases'
const app = rel.app || {}
const mirror = rel.mirror && app.file ? rel.mirror.replace(/\/?$/, '/') + app.file : ''
const size = app.size ? (app.size / 1024 / 1024).toFixed(1) + ' MB' : ''
</script>

::: tip 国内直链
本站为中国大陆用户提供**国内直链**，无需代理即可高速下载。直链与 GitHub Releases 分发的是同一份经过数字签名的文件，可使用下方的 SHA-256 自行核对。
:::

<DownloadCard inline />

## 文件信息

<table class="dl-table">
  <thead>
    <tr><th>文件</th><th>版本</th><th>大小</th><th>下载</th></tr>
  </thead>
  <tbody>
    <tr>
      <td><code>Campfire.exe</code></td>
      <td>{{ rel.version || '—' }}</td>
      <td>{{ size || '—' }}</td>
      <td><template v-if="mirror"><a :href="mirror">国内直链</a> · </template><a :href="GH" target="_blank" rel="noopener">GitHub</a></td>
    </tr>
  </tbody>
</table>

<div v-if="app.sha256" class="dl-sum">

**SHA-256**

<code>{{ app.sha256 }}</code>

</div>

请仅从本站或 GitHub Releases 获取 Campfire，切勿使用来源不明的二次分发版本。核对方法：在文件所在目录打开终端，执行

```powershell
certutil -hashfile Campfire.exe SHA256
```

输出的值应与上方完全一致。

## 系统要求

| 项目 | 要求 |
| --- | --- |
| 操作系统 | Windows 7 SP1、8、8.1、10、11 |
| 运行库 | .NET Framework 4.8；Windows 10 与 11 已内置 |
| 多人联机 | Windows 7 需另行安装 KB3063858 与 KB4474419 更新 |
| 磁盘空间 | 程序约 1.3 MB；首次联机时另需约 14 MB 存放联机组件 |

Windows 7 与 8 如尚未安装 .NET Framework 4.8，请从[微软官方网站](https://dotnet.microsoft.com/download/dotnet-framework/net48)下载。

## 安装与更新

Campfire 是免安装的单文件程序，下载后可直接运行，也可以放在任意文件夹中长期使用。个人数据保存在 `%LocalAppData%\Campfire`，与程序所在位置无关，移动或替换程序文件不会丢失设置。

正式版在启动时检查更新。发现新版本后，Campfire 会先征求同意，再下载更新包并校验其数字签名，校验通过后自动替换并重新启动。若程序位于不可写入的文件夹，将改为打开本页供手动下载。

完整的首次使用说明见[安装与上手](/guide/getting-started)。

## 下载遇到问题

- **直链下载缓慢或中断**：更换网络后重试，或改用 GitHub 渠道。
- **浏览器或系统提示文件不常见**：新发布的版本下载量尚少，可能触发 SmartScreen 等信誉提示。核对 SHA-256 一致后，选择“保留”或“仍要运行”即可。
- **杀毒软件拦截**：修改其他程序文件的工具偶尔会被误判。Campfire 完全开源，可审阅源代码或[自行构建](https://github.com/Xocio/Campfire#从源码构建)。
- **无法启动**：请确认系统已安装 .NET Framework 4.8，其他问题见[常见问题](/guide/faq)。
