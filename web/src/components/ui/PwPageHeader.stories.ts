import type { Meta, StoryObj } from '@storybook/vue3-vite'
import PwBadge from './PwBadge.vue'
import PwButton from './PwButton.vue'
import PwPageHeader from './PwPageHeader.vue'

/**
 * The top of every tool page: one `<h1>`, one sentence saying what the page does, and a rule.
 *
 * The sentence is not decoration. Each of these pages is a single operation with an HTTP
 * endpoint behind it, and the description is where the user finds out which one and what it
 * costs them before they upload anything.
 */
const meta = {
  title: 'Layout/Page header',
  component: PwPageHeader,
  tags: ['autodocs'],
  args: {
    title: 'Form fields',
    description:
      'Add, move and remove AcroForm fields on an existing PDF, then fill them or flatten them away.',
  },
} satisfies Meta<typeof PwPageHeader>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = {}

export const TitleOnly: Story = { args: { description: undefined } }

/** The meta slot carries the endpoint and its cost, so the API is visible from the UI. */
export const WithMeta: Story = {
  render: (args) => ({
    components: { PwPageHeader, PwBadge },
    setup: () => ({ args }),
    template: `
      <PwPageHeader v-bind="args">
        <template #meta>
          <div style="display:flex;gap:8px;align-items:center;margin-top:12px">
            <PwBadge mono tone="accent">POST</PwBadge>
            <code style="font-size:12px;color:var(--fg-muted)">/v1/forms/fields</code>
            <PwBadge dot tone="ok">18/20 left</PwBadge>
          </div>
        </template>
      </PwPageHeader>
    `,
  }),
}

/** An action in the header applies to the page, not to any one card on it. */
export const WithAction: Story = {
  render: (args) => ({
    components: { PwPageHeader, PwButton },
    setup: () => ({ args }),
    template: `
      <PwPageHeader v-bind="args">
        <template #actions><PwButton size="sm" href="/docs">API reference</PwButton></template>
      </PwPageHeader>
    `,
  }),
}
