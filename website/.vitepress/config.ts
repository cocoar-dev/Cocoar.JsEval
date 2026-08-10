import { defineConfig } from 'vitepress'
import { withMermaid } from 'vitepress-plugin-mermaid'
import llmstxt from 'vitepress-plugin-llms'

export default withMermaid(
  defineConfig({
    title: 'Cocoar.JsEval',
    description: 'JavaScript/TypeScript execution library for .NET, built on Jint',

    head: [
      ['link', { rel: 'icon', type: 'image/svg+xml', href: '/logo_light.svg' }],
      ['link', { rel: 'alternate', type: 'text/plain', href: '/llms.txt', title: 'LLM documentation (summary)' }],
      ['link', { rel: 'alternate', type: 'text/plain', href: '/llms-full.txt', title: 'LLM documentation (full)' }],
    ],

    vite: {
      plugins: [llmstxt({
        excludeUnnecessaryFiles: false,
        ignoreFiles: ['changelog.md'],
      })],
      optimizeDeps: {
        include: ['mermaid', 'dayjs'],
      },
    },

    themeConfig: {
      logo: {
        light: '/logo_light.svg',
        dark: '/logo_dark.svg',
      },

      siteTitle: 'Cocoar.JsEval',

      nav: [
        { text: 'Guide', link: '/guide/getting-started' },
        { text: 'Reference', link: '/reference/api' },
        { text: 'Changelog', link: '/changelog' },
        { text: 'LLM Docs', link: '/llms-full.txt', target: '_blank' },
        { text: 'NuGet', link: 'https://www.nuget.org/packages/Cocoar.JsEval' },
      ],

      sidebar: {
        '/guide/': [
          {
            text: 'Introduction',
            items: [
              { text: 'Getting Started', link: '/guide/getting-started' },
              { text: 'Architecture', link: '/guide/architecture' },
            ],
          },
          {
            text: 'Engine',
            items: [
              { text: 'JsEngine', link: '/guide/engine' },
              { text: 'TypeScript', link: '/guide/typescript' },
              { text: 'fetch() API', link: '/guide/fetch' },
              { text: 'Performance', link: '/guide/performance' },
            ],
          },
          {
            text: 'Modules',
            items: [
              { text: 'Overview', link: '/guide/modules' },
              { text: 'HTTP', link: '/guide/module-http' },
              { text: 'Database', link: '/guide/module-database' },
              { text: 'SMTP', link: '/guide/module-smtp' },
              { text: 'Template', link: '/guide/module-template' },
              { text: 'Other Modules', link: '/guide/modules-other' },
            ],
          },
          {
            text: 'Advanced',
            items: [
              { text: 'JS → LINQ (IQueryable)', link: '/guide/linq' },
              { text: 'TypeScript Definitions (.d.ts)', link: '/guide/ts-definitions' },
              { text: 'Custom Modules <span class="badge-adv" title="Advanced topic"></span>', link: '/guide/custom-modules' },
              { text: 'Module Tagging <span class="badge-adv" title="Advanced topic"></span>', link: '/guide/module-tagging' },
            ],
          },
        ],
        '/reference/': [
          {
            text: 'Reference',
            items: [
              { text: 'API Overview', link: '/reference/api' },
              { text: 'Packages', link: '/reference/packages' },
            ],
          },
        ],
      },

      socialLinks: [
        { icon: 'github', link: 'https://github.com/cocoar-dev/Cocoar.JsEval' },
      ],

      search: {
        provider: 'local',
      },

      footer: {
        message: 'Released under the Apache License 2.0.',
        copyright: 'Copyright 2025-present Cocoar',
      },
    },

    mermaid: {},

    mermaidPlugin: {
      class: 'mermaid',
    },
  }),
)
