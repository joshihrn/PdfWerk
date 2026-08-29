import type { StorybookConfig } from '@storybook/vue3-vite'

/**
 * Storybook for the PdfWerk design system.
 *
 * Scoped to `src/components/ui` on purpose. Stories exist to document and exercise the
 * primitives in isolation; the views that compose them are covered end to end by Playwright,
 * where they can talk to a real API. Rendering a view here would mean mocking the service, and
 * a mocked view proves less than the real one does.
 */
const config: StorybookConfig = {
  stories: ['../src/components/ui/**/*.stories.ts'],

  addons: [
    '@storybook/addon-docs',

    // Contrast, labelling and roles are load-bearing here: the components are meant to be
    // embedded in other people's applications, where an inaccessible control becomes their
    // problem. Catching it per-component is cheaper than auditing every page.
    '@storybook/addon-a11y',
  ],

  framework: {
    name: '@storybook/vue3-vite',
    options: {},
  },

  // Off by default. Storybook's telemetry is anonymous, but this is a public repository and
  // running the design system should not send anything anywhere from a contributor's machine
  // without them choosing it.
  core: { disableTelemetry: true },
}

export default config
