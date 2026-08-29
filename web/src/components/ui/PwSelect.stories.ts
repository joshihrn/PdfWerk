import type { Meta, StoryObj } from '@storybook/vue3-vite'
import PwSelect from './PwSelect.vue'

/**
 * A native `<select>` underneath, styled to match the rest of the controls.
 *
 * Native on purpose. A custom listbox has to reimplement typeahead, keyboard navigation and the
 * platform picker on a phone, and it will be worse than the one the operating system already
 * provides. The only thing worth replacing is the arrow.
 */
const meta = {
  title: 'Controls/Select',
  component: PwSelect,
  tags: ['autodocs'],
  args: {
    disabled: false,
    invalid: false,
    options: [
      { value: 'A4', label: 'A4' },
      { value: 'Letter', label: 'Letter' },
      { value: 'Legal', label: 'Legal' },
      { value: 'A3', label: 'A3' },
    ],
  },
  render: (args) => ({
    components: { PwSelect },
    setup: () => ({ args }),
    template: '<div style="max-width:220px"><PwSelect v-bind="args" /></div>',
  }),
} satisfies Meta<typeof PwSelect>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = {}

/** An option can be disabled individually, for a choice that exists but is not available yet. */
export const WithDisabledOption: Story = {
  args: {
    options: [
      { value: 'gemini', label: 'Gemini' },
      { value: 'groq', label: 'Groq' },
      { value: 'ollama', label: 'Ollama (not configured)', disabled: true },
    ],
  },
}

export const Invalid: Story = { args: { invalid: true } }

export const Disabled: Story = { args: { disabled: true } }
