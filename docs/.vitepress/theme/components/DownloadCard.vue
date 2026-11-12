<script setup lang="ts">
import { computed } from 'vue'
import { useData } from 'vitepress'
import rel from '../../release.json'

/**
 * 下载区：主按钮指向国内直链；尚未同步直链时改为 GitHub Releases。
 * android 为真时显示 Android 版，数据来自 release.json 的 android 字段，没有该字段时显示“即将发布”。
 */

const GH_LATEST = 'https://github.com/Xocio/Campfire/releases/latest'
const GH_TAG = 'https://github.com/Xocio/Campfire/releases/tag/'
const release = rel as Record<string, any>

/** android：显示 Android 版；inline：保留的旧参数 */
const props = defineProps<{ inline?: boolean; android?: boolean }>()

const { lang } = useData()
const en = computed(() => lang.value.startsWith('en'))
const hant = computed(() => lang.value.startsWith('zh-Hant'))
const yue = computed(() => lang.value.startsWith('yue'))

/** 当前显示的版本信息：桌面版取 release.json 本身，Android 版取其中的 android 字段 */
const entry = computed<Record<string, any> | null>(() =>
  props.android
    ? release.android ?? null
    : { version: release.version, date: release.date, file: release.app?.file }
)

const mirrorUrl = computed(() =>
  release.mirror && entry.value?.file ? release.mirror.replace(/\/?$/, '/') + entry.value.file : ''
)

const githubUrl = computed(() =>
  props.android && entry.value?.tag ? GH_TAG + entry.value.tag : GH_LATEST
)

const desktopText = computed(() =>
  yue.value
    ? {
        main: '下載 Campfire.exe',
        alt: '由 GitHub 下載',
        meta: 'Windows 7 SP1 及以上 · .NET Framework 4.8',
        single: '單檔案程式，唔使安裝'
      }
    : hant.value
    ? {
        main: '下載 Campfire.exe',
        alt: '從 GitHub 下載',
        meta: 'Windows 7 SP1 及以上 · .NET Framework 4.8',
        single: '單一檔案程式，無需安裝'
      }
    : en.value
    ? {
        main: 'Download Campfire.exe',
        alt: 'Download from GitHub',
        meta: 'Windows 7 SP1 and later · .NET Framework 4.8',
        single: 'Single executable, no installation required'
      }
    : {
        main: '下载 Campfire.exe',
        alt: '从 GitHub 下载',
        meta: 'Windows 7 SP1 及以上 · .NET Framework 4.8',
        single: '单文件程序，无需安装'
      }
)

const androidText = computed(() =>
  yue.value
    ? {
        main: '下載 Campfire.apk',
        alt: '由 GitHub 下載',
        meta: 'Android 7.0 及以上 · 64 位 ARM 處理器',
        single: '安裝包，需要允許安裝未知來源應用'
      }
    : hant.value
    ? {
        main: '下載 Campfire.apk',
        alt: '從 GitHub 下載',
        meta: 'Android 7.0 及以上 · 64 位元 ARM 處理器',
        single: '安裝套件，需要允許安裝未知來源應用程式'
      }
    : en.value
    ? {
        main: 'Download Campfire.apk',
        alt: 'Download from GitHub',
        meta: 'Android 7.0 and later · 64-bit ARM',
        single: 'APK package; installing apps from unknown sources must be allowed'
      }
    : {
        main: '下载 Campfire.apk',
        alt: '从 GitHub 下载',
        meta: 'Android 7.0 及以上 · 64 位 ARM 处理器',
        single: '安装包，需要允许安装未知来源应用'
      }
)

const t = computed(() => (props.android ? androidText.value : desktopText.value))

/** 尚未发布时按钮位置显示的文字 */
const soon = computed(() => (yue.value || hant.value ? '即將發布' : en.value ? 'Coming soon' : '即将发布'))
</script>

<template>
  <div class="dl vp-raw">
    <div class="dl-top">
      <span class="dl-os">
        <svg v-if="props.android" class="dl-icon" viewBox="0 0 24 24" aria-hidden="true">
          <path fill="currentColor" d="M17.6 9.48l1.84-3.18a.38.38 0 0 0-.66-.38l-1.86 3.22A11.5 11.5 0 0 0 12 8.1c-1.76 0-3.42.38-4.92 1.04L5.22 5.92a.38.38 0 0 0-.66.38L6.4 9.48A10.8 10.8 0 0 0 1 18h22a10.8 10.8 0 0 0-5.4-8.52zM7 15.25a1.25 1.25 0 1 1 0-2.5 1.25 1.25 0 0 1 0 2.5zm10 0a1.25 1.25 0 1 1 0-2.5 1.25 1.25 0 0 1 0 2.5z" />
        </svg>
        <svg v-else class="dl-icon" viewBox="0 0 24 24" aria-hidden="true">
          <path fill="currentColor" d="M3 3h8.5v8.5H3zM12.5 3H21v8.5h-8.5zM3 12.5h8.5V21H3zM12.5 12.5H21V21h-8.5z" />
        </svg>
        {{ props.android ? 'Android' : 'Windows' }}
      </span>
      <span v-if="entry?.version" class="dl-ver">{{ entry.version }}<template v-if="entry.date"> · {{ entry.date }}</template></span>
    </div>

    <a v-if="entry" class="dl-btn" :href="mirrorUrl || githubUrl" :target="mirrorUrl ? undefined : '_blank'">
      {{ t.main }}
    </a>
    <span v-else class="dl-btn is-off">{{ soon }}</span>

    <p class="dl-meta">{{ t.meta }}</p>
    <p class="dl-meta">{{ t.single }}</p>

    <a v-if="entry && mirrorUrl" class="dl-alt" :href="githubUrl" target="_blank" rel="noopener">{{ t.alt }}</a>
  </div>
</template>

<style scoped>
.dl {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  padding: 20px;
  border: 1px solid var(--vp-c-divider);
  border-radius: 10px;
  background: var(--vp-c-bg-soft);
}

.dl-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  min-height: 26px;
  gap: 12px;
  width: 100%;
  margin-bottom: 16px;
}

.dl-os {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  font-size: 17px;
  font-weight: 600;
  line-height: 1.4;
  color: var(--vp-c-text-1);
}

.dl-icon {
  width: 18px;
  height: 18px;
  color: var(--vp-c-text-2);
}

.dl-ver {
  font-size: 13px;
  color: var(--vp-c-text-3);
  white-space: nowrap;
}

.dl-btn {
  display: block;
  width: 100%;
  padding: 0 16px;
  border-radius: 8px;
  font-size: 15px;
  font-weight: 600;
  line-height: 44px;
  text-align: center;
  color: var(--vp-button-brand-text);
  background: var(--vp-button-brand-bg);
  text-decoration: none;
  transition: background-color .2s, color .2s;
}

.dl-btn:hover {
  color: var(--vp-button-brand-hover-text);
  background: var(--vp-button-brand-hover-bg);
}

.dl-btn.is-off,
.dl-btn.is-off:hover {
  color: var(--vp-c-text-3);
  background: var(--vp-c-default-soft);
  cursor: default;
}

.dl-btn:focus-visible,
.dl-alt:focus-visible {
  outline: 2px solid var(--vp-c-brand-1);
  outline-offset: 2px;
}

.dl-meta {
  margin: 0;
  font-size: 13px;
  line-height: 1.7;
  color: var(--vp-c-text-2);
}

.dl-btn + .dl-meta {
  margin-top: 14px;
}

.dl-alt {
  margin-top: 10px;
  font-size: 13px;
  font-weight: 500;
  color: var(--vp-c-brand-1);
  text-decoration: none;
  transition: color .2s;
}

.dl-alt:hover {
  color: var(--vp-c-brand-2);
}

@media (prefers-reduced-motion: reduce) {
  .dl-btn,
  .dl-alt {
    transition: none;
  }
}
</style>
