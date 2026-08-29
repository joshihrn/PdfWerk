import type { Meta, StoryObj } from '@storybook/vue3-vite'
import PwInput from './PwInput.vue'

/**
 * Text input. Recessed rather than raised: inputs are holes you put things into, buttons are
 * surfaces you press. Getting that pair the wrong way round is most of what makes a form feel
 * like a wireframe.
 */
const meta = {
  title: 'Controls/Input',
  component: PwInput,
  tags: ['autodocs'],
  args: {
    placeholder: 'Quarterly report',
    disabled: false,
    readonly: false,
    invalid: false,
    mono: false,
  },
  render: (args) => ({
    components: { PwInput },
    setup: () => ({ args }),
    template: '<div style="max-width:320px"><PwInput v-bind="args" /></div>',
  }),
} satisfies Meta<typeof PwInput>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = {}

/** For anything compared character by character: keys, field names, coordinates. */
export const Monospace: Story = { args: { mono: true, placeholder: 'pw_...' } }

export const Numeric: Story = {
  args: { type: 'number', placeholder: '0', min: 0, max: 842, step: 1 },
}

/**
 * `invalid` only paints the border. The message belongs to PwField, which owns the
 * `aria-describedby` wiring: a red outline on its own tells a screen reader nothing.
 */
export const Invalid: Story = { args: { invalid: true } }

export const Readonly: Story = { args: { readonly: true, mono: true } }

export const Disabled: Story = { args: { disabled: true } }
