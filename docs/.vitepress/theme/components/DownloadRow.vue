<script setup lang="ts">
import DownloadCard from './DownloadCard.vue'

/** Windows 版与 Android 版的下载卡片，宽屏并排一行，窄屏上下排列。 */
</script>

<template>
  <div class="dl-row vp-raw">
    <DownloadCard />
    <DownloadCard android />
  </div>
</template>

<style scoped>
.dl-row {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 16px;
  margin: 20px 0 8px;
}

@media (max-width: 640px) {
  .dl-row {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
