import type { Meta, StoryObj } from '@storybook/vue3-vite'
import PwButton from './PwButton.vue'
import PwCallout from './PwCallout.vue'

/**
 * Inline messages.
 *
 * The tone chooses the live region as well as the colour. `bad` is assertive, so a screen
 * reader interrupts and the user hears that their upload failed; everything else is polite and
 * waits its turn. Colour alone would leave that entirely unsaid.
 */
const meta = {
  title: 'Feedback/Callout',
  component: PwCallout,
  tags: ['autodocs'],
  argTypes: { tone: { control: 'inline-radio', options: ['info', 'ok', 'warn', 'bad'] } },
  args: { tone: 'info', assertive: false },
  render: (args) => ({
    components: { PwCallout },
    setup: () => ({ args }),
    template: `
      <div style="max-width:520px">
        <PwCallout v-bind="args">Anonymous callers get 5 requests a minute. A free key raises that to 20.</PwCallout>
      </div>
    `,
  }),
} satisfies Meta<typeof PwCallout>

export default meta
type Story = StoryObj<typeof meta>

export const Info: Story = {}

export const Success: Story = {
  args: { tone: 'ok', title: 'Document created' },
  render: (args) => ({
    components: { PwCallout },
    setup: () => ({ args }),
    template: `
      <div style="max-width:520px">
        <PwCallout v-bind="args">quarterly-report.pdf, 4 pages, 82 KB.</PwCallout>
      </div>
    `,
  }),
}

export const Warning: Story = {
  args: { tone: 'warn', title: 'Copy this now' },
  render: (args) => ({
    components: { PwCallout },
    setup: () => ({ args }),
    template: `
      <div style="max-width:520px">
        <PwCallout v-bind="args">This key is shown once and cannot be retrieved again.</PwCallout>
      </div>
    `,
  }),
}

/** Carries `role="alert"`, so it interrupts rather than waiting to be noticed. */
export const Error: Story = {
  args: { tone: 'bad', title: 'Could not read that file' },
  render: (args) => ({
    components: { PwCallout },
    setup: () => ({ args }),
    template: `
      <div style="max-width:520px">
        <PwCallout v-bind="args">The upload is not a PDF, or the file is corrupt.</PwCallout>
      </div>
    `,
  }),
}

/** An action belongs in the callout when it is the way out of the state being described. */
export const WithAction: Story = {
  args: { tone: 'warn', title: 'Rate limit reached' },
  render: (args) => ({
    components: { PwCallout, PwButton },
    setup: () => ({ args }),
    template: `
      <div style="max-width:520px">
        <PwCallout v-bind="args">
          Try again in 34 seconds, or create a free key to raise the ceiling.
          <template #actions><PwButton size="sm" href="/api">Get a key</PwButton></template>
        </PwCallout>
      </div>
    `,
  }),
}

/** All four together, which is the view to check when adjusting tone tokens. */
export const EveryTone: Story = {
  render: () => ({
    components: { PwCallout },
    setup: () => ({ tones: ['info', 'ok', 'warn', 'bad'] }),
    template: `
      <div style="max-width:520px;display:flex;flex-direction:column;gap:12px">
        <PwCallout v-for="tone in tones" :key="tone" :tone="tone" :title="tone">
          The quick brown fox jumps over the lazy dog.
        </PwCallout>
      </div>
    `,
  }),
}
