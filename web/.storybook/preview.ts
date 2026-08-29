import type { Preview } from '@storybook/vue3-vite'
import '../src/styles/tokens.css'
import '../src/styles/base.css'

/**
 * The theme is an attribute on <html>, so the toolbar has to set it there rather than wrap the
 * story in a themed div — components read tokens that cascade from the root, and a wrapper
 * would leave the canvas background out of step with the component sitting on it.
 */
function applyTheme(theme: string) {
  const root = document.documentElement

  if (theme === 'system') root.removeAttribute('data-theme')
  else root.setAttribute('data-theme', theme)
}

const preview: Preview = {
  parameters: {
    controls: { matchers: { color: /(background|color)$/i } },

    // The design system is light-first; dark is a real target, not an afterthought, so both
    // get a backdrop that matches the app rather than Storybook's default white.
    backgrounds: { disable: true },

    a11y: { test: 'error' },
  },

  globalTypes: {
    theme: {
      description: 'Colour scheme',
      toolbar: {
        title: 'Theme',
        icon: 'circlehollow',
        items: [
          { value: 'light', title: 'Light' },
          { value: 'dark', title: 'Dark' },
          { value: 'system', title: 'System' },
        ],
        dynamicTitle: true,
      },
    },
  },

  initialGlobals: { theme: 'light' },

  decorators: [
    (story, context) => {
      applyTheme(context.globals.theme)

      return {
        components: { story },
        template: '<div style="padding:24px;background:var(--bg);color:var(--fg)"><story /></div>',
      }
    },
  ],
}

export default preview
