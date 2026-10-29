import { defineConfig } from 'vitepress'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

// 版本信息由发布流水线写入 release.json，导航中的版本号随之更新
const release = JSON.parse(
  readFileSync(fileURLToPath(new URL('./release.json', import.meta.url)), 'utf-8')
)

const repo = 'https://github.com/Xocio/Campfire'

// 尚未发布时不显示版本菜单
const versionMenu = (zh: boolean) =>
  release.version
    ? [
        {
          text: release.version,
          items: [
            { text: zh ? '更新日志' : 'Changelog', link: `${repo}/blob/main/CHANGELOG.md` },
            { text: zh ? '历史版本' : 'All releases', link: `${repo}/releases` }
          ]
        }
      ]
    : []

export default defineConfig({
  // 部署到子路径（如 GitHub Pages 项目页）时设置 DOCS_BASE=/Campfire/
  base: process.env.DOCS_BASE ?? '/',
  title: 'Campfire',
  description: '先进的 Minecraft 工具箱',
  lang: 'zh-CN',
  cleanUrls: true,
  lastUpdated: true,
  srcExclude: ['README*.md', 'node_modules/**'],

  head: [
    ['link', { rel: 'icon', type: 'image/png', href: '/logo.png' }],
    ['meta', { name: 'theme-color', content: '#e2680c' }]
  ],

  themeConfig: {
    logo: '/logo.gif',
    socialLinks: [
      { icon: 'github', link: repo },
      { icon: 'telegram', link: 'https://t.me/CampfireGroup', ariaLabel: 'Telegram' },
      { icon: 'telegram', link: 'https://t.me/McCampfire', ariaLabel: 'Telegram Channel' }
    ],
    search: {
      provider: 'local',
      options: {
        locales: {
          root: {
            translations: {
              button: { buttonText: '搜索文档', buttonAriaLabel: '搜索文档' },
              modal: {
                displayDetails: '显示详细列表',
                resetButtonTitle: '清除查询条件',
                backButtonTitle: '关闭搜索',
                noResultsText: '没有找到相关结果',
                footer: {
                  selectText: '选择',
                  navigateText: '切换',
                  closeText: '关闭'
                }
              }
            }
          }
        }
      }
    }
  },

  locales: {
    root: {
      label: '简体中文',
      lang: 'zh-CN',
      themeConfig: {
        nav: [
          { text: '指南', link: '/guide/what-is-campfire', activeMatch: '^/guide/(?!multiplayer|room|lobby|network)' },
          { text: '多人联机', link: '/guide/multiplayer', activeMatch: '^/guide/(multiplayer|room|lobby|network)' },
          { text: '下载', link: '/download' },
          ...versionMenu(true)
        ],
        sidebar: {
          '/guide/': [
            {
              text: '入门',
              items: [
                { text: '认识 Campfire', link: '/guide/what-is-campfire' },
                { text: '安装与上手', link: '/guide/getting-started' }
              ]
            },
            {
              text: '多人联机',
              items: [
                { text: '创建与加入房间', link: '/guide/multiplayer' },
                { text: '联机大厅', link: '/guide/lobby' },
                { text: '房间管理', link: '/guide/room' },
                { text: '网络与隐私', link: '/guide/network' }
              ]
            },
            {
              text: '地图',
              items: [{ text: '浏览与导入地图', link: '/guide/maps' }]
            },
            {
              text: '工具',
              items: [
                { text: '启动器修复', link: '/guide/launcher-repair' },
                { text: 'JRE 启用工具', link: '/guide/jre' }
              ]
            },
            {
              text: '设置',
              items: [
                { text: '个性化', link: '/guide/personalization' },
                { text: '更新与隐私', link: '/guide/updates-privacy' }
              ]
            },
            {
              text: '深入',
              items: [
                { text: '工作原理', link: '/guide/how-it-works' },
                { text: '联网与安全', link: '/guide/security' },
                { text: '常见问题', link: '/guide/faq' }
              ]
            }
          ]
        },
        editLink: {
          pattern: `${repo}/edit/main/docs/:path`,
          text: '在 GitHub 上编辑此页'
        },
        footer: {
          message: 'Campfire 是开源免费的社区项目，与 Mojang Studios 及微软均无关联。<br />在线服务由 <a href="https://www.cloudflare.com/" target="_blank" rel="noopener">Cloudflare</a> 提供服务与安全防护。',
          copyright: 'Copyright © 2026 Xocio'
        },
        docFooter: { prev: '上一页', next: '下一页' },
        outline: { label: '本页目录', level: [2, 3] },
        lastUpdated: { text: '最后更新' },
        notFound: {
          title: '页面不存在',
          quote: '该页面可能已被移动或删除。',
          linkLabel: '返回首页',
          linkText: '返回首页'
        },
        langMenuLabel: '切换语言',
        returnToTopLabel: '回到顶部',
        sidebarMenuLabel: '菜单',
        darkModeSwitchLabel: '外观',
        lightModeSwitchTitle: '切换到浅色模式',
        darkModeSwitchTitle: '切换到深色模式',
        skipToContentLabel: '跳到正文'
      }
    },

    en: {
      label: 'English',
      lang: 'en-US',
      link: '/en/',
      description: 'An advanced Minecraft toolbox',
      themeConfig: {
        nav: [
          { text: 'Guide', link: '/en/guide/what-is-campfire', activeMatch: '^/en/guide/(?!multiplayer|room|lobby|network)' },
          { text: 'Multiplayer', link: '/en/guide/multiplayer', activeMatch: '^/en/guide/(multiplayer|room|lobby|network)' },
          { text: 'Download', link: '/en/download' },
          ...versionMenu(false)
        ],
        sidebar: {
          '/en/guide/': [
            {
              text: 'Introduction',
              items: [
                { text: 'What is Campfire', link: '/en/guide/what-is-campfire' },
                { text: 'Getting Started', link: '/en/guide/getting-started' }
              ]
            },
            {
              text: 'Multiplayer',
              items: [
                { text: 'Hosting and Joining', link: '/en/guide/multiplayer' },
                { text: 'Lobby', link: '/en/guide/lobby' },
                { text: 'Managing a Room', link: '/en/guide/room' },
                { text: 'Network and Privacy', link: '/en/guide/network' }
              ]
            },
            {
              text: 'Maps',
              items: [{ text: 'Browsing and Importing Maps', link: '/en/guide/maps' }]
            },
            {
              text: 'Tools',
              items: [
                { text: 'Launcher Repair', link: '/en/guide/launcher-repair' },
                { text: 'JRE Tool', link: '/en/guide/jre' }
              ]
            },
            {
              text: 'Settings',
              items: [
                { text: 'Personalization', link: '/en/guide/personalization' },
                { text: 'Updates and Privacy', link: '/en/guide/updates-privacy' }
              ]
            },
            {
              text: 'In Depth',
              items: [
                { text: 'How It Works', link: '/en/guide/how-it-works' },
                { text: 'Network Security', link: '/en/guide/security' },
                { text: 'FAQ', link: '/en/guide/faq' }
              ]
            }
          ]
        },
        editLink: {
          pattern: `${repo}/edit/main/docs/:path`,
          text: 'Edit this page on GitHub'
        },
        footer: {
          message: 'Campfire is a free, open-source community project, not affiliated with Mojang Studios or Microsoft.<br />Online services are provided and protected by <a href="https://www.cloudflare.com/" target="_blank" rel="noopener">Cloudflare</a>.',
          copyright: 'Copyright © 2026 Xocio'
        }
      }
    }
  }
})
