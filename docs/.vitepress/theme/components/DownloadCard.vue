<script setup lang="ts">
import { computed } from 'vue'
import { useData } from 'vitepress'
import rel from '../../release.json'

/**
 * 下载区：主按钮指向国内直链；尚未同步直链时改为 GitHub Releases。
 * android 为真时显示安卓版，数据来自 release.json 的 android 字段，没有该字段时不显示。
 */

const GH_LATEST = 'https://github.com/Xocio/Campfire/releases/latest'
const GH_TAG = 'https://github.com/Xocio/Campfire/releases/tag/'
const release = rel as Record<string, any>

/** inline：放在文档正文里时使用，带浅底并在顶部显示版本号；android：显示安卓版 */
const props = defineProps<{ inline?: boolean; android?: boolean }>()

const { lang } = useData()
const en = computed(() => lang.value.startsWith('en'))
const hant = computed(() => lang.value.startsWith('zh-Hant'))
const yue = computed(() => lang.value.startsWith('yue'))

/** 当前显示的版本信息：桌面版取 release.json 本身，安卓版取其中的 android 字段 */
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
        main: '下載 Campfire',
        alt: '由 GitHub 下載',
        meta: 'Windows 7 SP1 及以上 · .NET Framework 4.8',
        single: '單檔案程式，唔使安裝'
      }
    : hant.value
    ? {
        main: '下載 Campfire',
        alt: '從 GitHub 下載',
        meta: 'Windows 7 SP1 及以上 · .NET Framework 4.8',
        single: '單一檔案程式，無需安裝'
      }
    : en.value
    ? {
        main: 'Download Campfire',
        alt: 'Download from GitHub',
        meta: 'Windows 7 SP1 and later · .NET Framework 4.8',
        single: 'Single executable, no installation required'
      }
    : {
        main: '下载 Campfire',
        alt: '从 GitHub 下载',
        meta: 'Windows 7 SP1 及以上 · .NET Framework 4.8',
        single: '单文件程序，无需安装'
      }
)

const androidText = computed(() =>
  yue.value
    ? {
        main: '下載安卓版',
        alt: '由 GitHub 下載',
        meta: 'Android 7.0 及以上 · 64 位 ARM 處理器',
        single: '安裝包，需要允許安裝未知來源應用'
      }
    : hant.value
    ? {
        main: '下載安卓版',
        alt: '從 GitHub 下載',
        meta: 'Android 7.0 及以上 · 64 位元 ARM 處理器',
        single: '安裝套件，需要允許安裝未知來源應用程式'
      }
    : en.value
    ? {
        main: 'Download for Android',
        alt: 'Download from GitHub',
        meta: 'Android 7.0 and later · 64-bit ARM',
        single: 'APK package; installing apps from unknown sources must be allowed'
      }
    : {
        main: '下载安卓版',
        alt: '从 GitHub 下载',
        meta: 'Android 7.0 及以上 · 64 位 ARM 处理器',
        single: '安装包，需要允许安装未知来源应用'
      }
)

const t = computed(() => (props.android ? androidText.value : desktopText.value))
</script>

<template>
  <div v-if="entry" class="dl vp-raw" :class="{ 'is-inline': props.inline, 'is-android': props.android }">
    <p v-if="(props.inline || props.android) && entry.version" class="dl-ver-line">
      {{ entry.version }}<template v-if="entry.date"> · {{ entry.date }}</template>
    </p>

    <a class="dl-btn" :href="mirrorUrl || githubUrl" :target="mirrorUrl ? undefined : '_blank'">
      {{ t.main }}
    </a>

    <p class="dl-meta">
      {{ t.meta }}
    </p>
    <p class="dl-meta">{{ t.single }}</p>

    <a v-if="mirrorUrl" class="dl-alt" :href="githubUrl" target="_blank" rel="noopener">{{ t.alt }}</a>
  </div>
</template>

<style scoped>
.dl {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
}

.dl-ver-line {
  margin: 0 0 20px;
  font-size: 13px;
  line-height: 1.5;
  color: var(--vp-c-text-3);
}

.dl-btn {
  display: inline-block;
  min-width: 220px;
  padding: 0 32px;
  border-radius: 999px;
  font-size: 15px;
  font-weight: 600;
  line-height: 48px;
  color: var(--vp-button-brand-text);
  background: var(--vp-button-brand-bg);
  box-shadow: 0 8px 24px -10px rgba(226, 104, 12, .7);
  text-decoration: none;
  transition: background-color .2s, box-shadow .2s, color .2s;
}

.dl-btn:hover {
  color: var(--vp-button-brand-hover-text);
  background: var(--vp-button-brand-hover-bg);
  box-shadow: 0 10px 28px -10px rgba(226, 104, 12, .85);
}

.dl-btn:focus-visible,
.dl-alt:focus-visible {
  outline: 2px solid var(--vp-c-brand-1);
  outline-offset: 2px;
}

.dl-meta {
  margin: 0;
  font-size: 13.5px;
  line-height: 1.7;
  color: var(--vp-c-text-2);
}

.dl-btn + .dl-meta {
  margin-top: 20px;
}

.dl-alt {
  margin-top: 14px;
  font-size: 14px;
  font-weight: 500;
  color: var(--vp-c-brand-1);
  text-decoration: none;
  transition: color .2s;
}

.dl-alt:hover {
  color: var(--vp-c-brand-2);
}

/* 安卓版：描边按钮，与桌面版的主按钮区分 */
.dl.is-android:not(.is-inline) {
  margin-top: 44px;
}

.dl.is-android .dl-btn {
  color: var(--vp-c-brand-1);
  background: transparent;
  box-shadow: inset 0 0 0 1.5px var(--vp-c-brand-1);
}

.dl.is-android .dl-btn:hover {
  color: var(--vp-c-brand-2);
  background: var(--vp-c-brand-soft);
  box-shadow: inset 0 0 0 1.5px var(--vp-c-brand-2);
}

/* 文档正文里：加一层浅底，和上下文分开 */
.dl.is-inline {
  margin: 20px 0 8px;
  padding: 36px 24px 36px;
  border-radius: 12px;
  background: var(--vp-c-bg-soft);
}

@media (max-width: 640px) {
  .dl-btn {
    width: 100%;
  }

  .dl.is-inline {
    padding: 28px 16px;
  }
}

@media (prefers-reduced-motion: reduce) {
  .dl-btn,
  .dl-alt {
    transition: none;
  }
}
</style>
