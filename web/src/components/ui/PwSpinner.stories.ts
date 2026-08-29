import type { Meta, StoryObj } from '@storybook/vue3-vite'
import PwSpinner from './PwSpinner.vue'

/**
 * A busy indicator, sized to sit inline with text.
 *
 * It carries `role="status"` and an accessible label, so waiting is announced rather than only
 * animated. Most of the time you want PwButton's `loading` instead, which handles this along
 * with blocking the click.
 */
const meta = {
  title: 'Feedback/Spinner',
  component: PwSpinner,
  tags: ['autodocs'],
  args: { size: 14, label: 'Loading' },
} satisfies Meta<typeof PwSpinner>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = {}

/** The border scales with the diameter, so a large one does not read as a thin ring. */
export const Sizes: Story = {
  render: () => ({
    components: { PwSpinner },
    setup: () => ({ sizes: [12, 14, 20, 32, 48] }),
    template: `
      <div style="display:flex;gap:20px;align-items:center">
        <PwSpinner v-for="size in sizes" :key="size" :size="size" />
      </div>
    `,
  }),
}

/** The label should say what is being waited on, not merely that something is. */
export const InContext: Story = {
  render: () => ({
    components: { PwSpinner },
    template: `
      <div style="display:flex;gap:8px;align-items:center;font-size:13px;color:var(--fg-muted)">
        <PwSpinner label="Summarising the document" />
        <span>Summarising, this one goes to a model and can take a few seconds</span>
      </div>
    `,
  }),
}
