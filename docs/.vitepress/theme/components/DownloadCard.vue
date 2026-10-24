<script setup lang="ts">
import { computed } from 'vue'
import { useData } from 'vitepress'
import rel from '../../release.json'

/**
 * 下载区：主按钮指向国内直链；尚未同步直链时改为 GitHub Releases。
 */

const GH_LATEST = 'https://github.com/Xocio/Campfire/releases/latest'
const release = rel as Record<string, any>

/** inline：放在文档正文里时使用，带浅底并在顶部显示版本号 */
const props = defineProps<{ inline?: boolean }>()

const { lang } = useData()
const en = computed(() => lang.value.startsWith('en'))

const mirrorUrl = computed(() =>
  release.mirror && release.app?.file ? release.mirror.replace(/\/?$/, '/') + release.app.file : ''
)

const size = computed(() => {
  const bytes = release.app?.size
  return bytes ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : ''
})

const t = computed(() =>
  en.value
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
</script>

<template>
  <div class="dl vp-raw" :class="{ 'is-inline': props.inline }">
    <p v-if="props.inline && release.version" class="dl-ver-line">
      {{ release.version }}<template v-if="release.date"> · {{ release.date }}</template>
    </p>

    <a class="dl-btn" :href="mirrorUrl || GH_LATEST" :target="mirrorUrl ? undefined : '_blank'">
      {{ t.main }}
    </a>

    <p class="dl-meta">
      {{ t.meta }}
      <template v-if="size"> · {{ size }}</template>
    </p>
    <p class="dl-meta">{{ t.single }}</p>

    <a v-if="mirrorUrl" class="dl-alt" :href="GH_LATEST" target="_blank" rel="noopener">{{ t.alt }}</a>
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
  transition: background-color .2s, box-shadow .2s;
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
