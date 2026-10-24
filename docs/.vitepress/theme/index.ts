import DefaultTheme from 'vitepress/theme'
import type { Theme } from 'vitepress'
import DownloadCard from './components/DownloadCard.vue'
import './custom.css'

export default {
  extends: DefaultTheme,
  enhanceApp({ app }) {
    app.component('DownloadCard', DownloadCard)
  }
} satisfies Theme
