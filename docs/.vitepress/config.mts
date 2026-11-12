import { defineConfig } from 'vitepress'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

// 版本信息由发布流水线写入 release.json，导航中的版本号随之更新
const release = JSON.parse(
  readFileSync(fileURLToPath(new URL('./release.json', import.meta.url)), 'utf-8')
)

const repo = 'https://github.com/Xocio/Campfire'

// 尚未发布时不显示版本菜单
const versionMenu = (names: string[]) =>
  release.version
    ? [
        {
          text: release.version,
          items: [
            { text: names[0], link: `${repo}/blob/main/CHANGELOG.md` },
            { text: names[1], link: `${repo}/releases` }
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
          },
          'zh-hant': {
            translations: {
              button: { buttonText: '搜尋文件', buttonAriaLabel: '搜尋文件' },
              modal: {
                displayDetails: '顯示詳細列表',
                resetButtonTitle: '清除查詢條件',
                backButtonTitle: '關閉搜尋',
                noResultsText: '沒有找到相關結果',
                footer: {
                  selectText: '選擇',
                  navigateText: '切換',
                  closeText: '關閉'
                }
              }
            }
          },
          'yue': {
            translations: {
              button: { buttonText: '搜索文檔', buttonAriaLabel: '搜索文檔' },
              modal: {
                displayDetails: '顯示詳細列表',
                resetButtonTitle: '清除查詢條件',
                backButtonTitle: '關閉搜索',
                noResultsText: '冇搵到相關結果',
                footer: {
                  selectText: '選擇',
                  navigateText: '切換',
                  closeText: '關閉'
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
          { text: '指南', link: '/guide/what-is-campfire', activeMatch: '^/guide/(?!multiplayer|room|lobby|mods|voice|network)' },
          { text: '多人联机', link: '/guide/multiplayer', activeMatch: '^/guide/(multiplayer|room|lobby|mods|voice|network)' },
          { text: '下载', link: '/download' },
          ...versionMenu(['更新日志', '历史版本'])
        ],
        sidebar: {
          '/guide/': [
            {
              text: '入门',
              items: [
                { text: '认识 Campfire', link: '/guide/what-is-campfire' },
                { text: '安装与上手', link: '/guide/getting-started' },
                { text: '安卓版', link: '/guide/android' }
              ]
            },
            {
              text: '多人联机',
              items: [
                { text: '创建与加入房间', link: '/guide/multiplayer' },
                { text: '联机大厅', link: '/guide/lobby' },
                { text: '房间管理', link: '/guide/room' },
                { text: '模组同步', link: '/guide/mods' },
                { text: '语音聊天', link: '/guide/voice' },
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

    'zh-hant': {
      label: '繁體中文',
      lang: 'zh-Hant',
      link: '/zh-hant/',
      description: '先進的 Minecraft 工具箱',
      themeConfig: {
        nav: [
          { text: '指南', link: '/zh-hant/guide/what-is-campfire', activeMatch: '^/zh-hant/guide/(?!multiplayer|room|lobby|mods|voice|network)' },
          { text: '多人連線', link: '/zh-hant/guide/multiplayer', activeMatch: '^/zh-hant/guide/(multiplayer|room|lobby|mods|voice|network)' },
          { text: '下載', link: '/zh-hant/download' },
          ...versionMenu(['更新日誌', '歷史版本'])
        ],
        sidebar: {
          '/zh-hant/guide/': [
            {
              text: '入門',
              items: [
                { text: '認識 Campfire', link: '/zh-hant/guide/what-is-campfire' },
                { text: '安裝與上手', link: '/zh-hant/guide/getting-started' },
                { text: '安卓版', link: '/zh-hant/guide/android' }
              ]
            },
            {
              text: '多人連線',
              items: [
                { text: '建立與加入房間', link: '/zh-hant/guide/multiplayer' },
                { text: '連線大廳', link: '/zh-hant/guide/lobby' },
                { text: '房間管理', link: '/zh-hant/guide/room' },
                { text: '模組同步', link: '/zh-hant/guide/mods' },
                { text: '語音聊天', link: '/zh-hant/guide/voice' },
                { text: '網路與隱私', link: '/zh-hant/guide/network' }
              ]
            },
            {
              text: '地圖',
              items: [{ text: '瀏覽與匯入地圖', link: '/zh-hant/guide/maps' }]
            },
            {
              text: '工具',
              items: [
                { text: '啟動器修復', link: '/zh-hant/guide/launcher-repair' },
                { text: 'JRE 啟用工具', link: '/zh-hant/guide/jre' }
              ]
            },
            {
              text: '設定',
              items: [
                { text: '個性化', link: '/zh-hant/guide/personalization' },
                { text: '更新與隱私', link: '/zh-hant/guide/updates-privacy' }
              ]
            },
            {
              text: '深入',
              items: [
                { text: '工作原理', link: '/zh-hant/guide/how-it-works' },
                { text: '連網與安全', link: '/zh-hant/guide/security' },
                { text: '常見問題', link: '/zh-hant/guide/faq' }
              ]
            }
          ]
        },
        editLink: {
          pattern: `${repo}/edit/main/docs/:path`,
          text: '在 GitHub 上編輯此頁'
        },
        footer: {
          message: 'Campfire 是開源免費的社群專案，與 Mojang Studios 及微軟均無關聯。<br />線上服務由 <a href="https://www.cloudflare.com/" target="_blank" rel="noopener">Cloudflare</a> 提供服務與安全防護。',
          copyright: 'Copyright © 2026 Xocio'
        },
        docFooter: { prev: '上一頁', next: '下一頁' },
        outline: { label: '本頁目錄', level: [2, 3] },
        lastUpdated: { text: '最後更新' },
        notFound: {
          title: '頁面不存在',
          quote: '該頁面可能已被移動或刪除。',
          linkLabel: '返回首頁',
          linkText: '返回首頁'
        },
        langMenuLabel: '切換語言',
        returnToTopLabel: '回到頂部',
        sidebarMenuLabel: '選單',
        darkModeSwitchLabel: '外觀',
        lightModeSwitchTitle: '切換到淺色模式',
        darkModeSwitchTitle: '切換到深色模式',
        skipToContentLabel: '跳到正文'
      }
    },

    'yue': {
      label: '粵語',
      lang: 'yue-Hant-HK',
      link: '/yue/',
      description: '先進嘅 Minecraft 工具箱',
      themeConfig: {
        nav: [
          { text: '指南', link: '/yue/guide/what-is-campfire', activeMatch: '^/yue/guide/(?!multiplayer|room|lobby|mods|voice|network)' },
          { text: '多人聯機', link: '/yue/guide/multiplayer', activeMatch: '^/yue/guide/(multiplayer|room|lobby|mods|voice|network)' },
          { text: '下載', link: '/yue/download' },
          ...versionMenu(['更新日誌', '歷史版本'])
        ],
        sidebar: {
          '/yue/guide/': [
            {
              text: '入門',
              items: [
                { text: '認識 Campfire', link: '/yue/guide/what-is-campfire' },
                { text: '安裝與上手', link: '/yue/guide/getting-started' },
                { text: '安卓版', link: '/yue/guide/android' }
              ]
            },
            {
              text: '多人聯機',
              items: [
                { text: '創建與加入房間', link: '/yue/guide/multiplayer' },
                { text: '聯機大廳', link: '/yue/guide/lobby' },
                { text: '房間管理', link: '/yue/guide/room' },
                { text: '模組同步', link: '/yue/guide/mods' },
                { text: '語音聊天', link: '/yue/guide/voice' },
                { text: '網絡與隱私', link: '/yue/guide/network' }
              ]
            },
            {
              text: '地圖',
              items: [{ text: '瀏覽與導入地圖', link: '/yue/guide/maps' }]
            },
            {
              text: '工具',
              items: [
                { text: '啟動器修復', link: '/yue/guide/launcher-repair' },
                { text: 'JRE 啟用工具', link: '/yue/guide/jre' }
              ]
            },
            {
              text: '設置',
              items: [
                { text: '個性化', link: '/yue/guide/personalization' },
                { text: '更新與隱私', link: '/yue/guide/updates-privacy' }
              ]
            },
            {
              text: '深入',
              items: [
                { text: '工作原理', link: '/yue/guide/how-it-works' },
                { text: '聯網與安全', link: '/yue/guide/security' },
                { text: '常見問題', link: '/yue/guide/faq' }
              ]
            }
          ]
        },
        editLink: {
          pattern: `${repo}/edit/main/docs/:path`,
          text: '喺 GitHub 上編輯此頁'
        },
        footer: {
          message: 'Campfire 係開源免費嘅社區項目，與 Mojang Studios 及微軟均無關聯。<br />在線服務由 <a href="https://www.cloudflare.com/" target="_blank" rel="noopener">Cloudflare</a> 提供服務與安全防護。',
          copyright: 'Copyright © 2026 Xocio'
        },
        docFooter: { prev: '上一頁', next: '下一頁' },
        outline: { label: '本頁目錄', level: [2, 3] },
        lastUpdated: { text: '最後更新' },
        notFound: {
          title: '頁面唔存在',
          quote: '該頁面可能已被移動或刪除。',
          linkLabel: '返回首頁',
          linkText: '返回首頁'
        },
        langMenuLabel: '切換語言',
        returnToTopLabel: '回到頂部',
        sidebarMenuLabel: '菜單',
        darkModeSwitchLabel: '外觀',
        lightModeSwitchTitle: '切換到淺色模式',
        darkModeSwitchTitle: '切換到深色模式',
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
          { text: 'Guide', link: '/en/guide/what-is-campfire', activeMatch: '^/en/guide/(?!multiplayer|room|lobby|mods|voice|network)' },
          { text: 'Multiplayer', link: '/en/guide/multiplayer', activeMatch: '^/en/guide/(multiplayer|room|lobby|mods|voice|network)' },
          { text: 'Download', link: '/en/download' },
          ...versionMenu(['Changelog', 'All releases'])
        ],
        sidebar: {
          '/en/guide/': [
            {
              text: 'Introduction',
              items: [
                { text: 'What is Campfire', link: '/en/guide/what-is-campfire' },
                { text: 'Getting Started', link: '/en/guide/getting-started' },
                { text: 'Android', link: '/en/guide/android' }
              ]
            },
            {
              text: 'Multiplayer',
              items: [
                { text: 'Hosting and Joining', link: '/en/guide/multiplayer' },
                { text: 'Lobby', link: '/en/guide/lobby' },
                { text: 'Managing a Room', link: '/en/guide/room' },
                { text: 'Mod Sync', link: '/en/guide/mods' },
                { text: 'Voice Chat', link: '/en/guide/voice' },
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
