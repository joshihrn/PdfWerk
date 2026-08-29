import type { Meta, StoryObj } from '@storybook/vue3-vite'
import PwTextarea from './PwTextarea.vue'

/**
 * Multi-line input. Monospaced by default, because almost everything typed into one here is
 * Markdown, HTML or a list of field values, and proportional type makes structure harder to see.
 */
const meta = {
  title: 'Controls/Textarea',
  component: PwTextarea,
  tags: ['autodocs'],
  args: {
    rows: 6,
    mono: true,
    disabled: false,
    invalid: false,
    placeholder: '# Quarterly report\n\nRevenue rose 12 per cent.',
  },
  render: (args) => ({
    components: { PwTextarea },
    setup: () => ({ args }),
    template: '<div style="max-width:520px"><PwTextarea v-bind="args" /></div>',
  }),
} satisfies Meta<typeof PwTextarea>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = {}

/** Proportional, for prose the user writes rather than markup they compose. */
export const Prose: Story = { args: { mono: false, placeholder: 'Tell us what went wrong' } }

export const Invalid: Story = { args: { invalid: true } }

export const Disabled: Story = { args: { disabled: true } }

/** Tall enough that a page of Markdown does not need scrolling to review. */
export const Tall: Story = { args: { rows: 14 } }
