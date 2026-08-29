import { ref } from 'vue'
import type { Meta, StoryObj } from '@storybook/vue3-vite'
import PwSegmented from './PwSegmented.vue'

/**
 * A row of mutually exclusive choices, for switching between modes rather than submitting a
 * value.
 *
 * Implemented as an ARIA tablist with roving focus: one tab stop for the whole group, arrow
 * keys to move between options, Home and End to jump to the ends. That is the behaviour a
 * keyboard user expects here, and a row of buttons does not provide it.
 */
const meta = {
  title: 'Controls/Segmented',
  component: PwSegmented,
  tags: ['autodocs'],
  args: {
    label: 'Form mode',
    modelValue: 'design',
    options: [
      { value: 'design', label: 'Design fields' },
      { value: 'fill', label: 'Fill values' },
    ],
  },
  render: (args) => ({
    components: { PwSegmented },
    setup: () => ({ args, model: ref(args.modelValue) }),
    template: '<PwSegmented v-bind="args" v-model="model" />',
  }),
} satisfies Meta<typeof PwSegmented>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = {}

/** A badge carries a count, so the option says how much is behind it before you switch. */
export const WithBadges: Story = {
  args: {
    label: 'Form mode',
    modelValue: 'design',
    options: [
      { value: 'design', label: 'Design fields', badge: '3' },
      { value: 'fill', label: 'Fill values', badge: '3' },
    ],
  },
}

/**
 * Three is about the ceiling. Past that the row starts to wrap and a select is the better
 * control, because scanning beats reading a wall of equally weighted options.
 */
export const ThreeOptions: Story = {
  args: {
    label: 'Delivery',
    modelValue: 'stream',
    options: [
      { value: 'stream', label: 'Preview' },
      { value: 'download', label: 'Download' },
      { value: 'json', label: 'JSON' },
    ],
  },
}
