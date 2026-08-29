import { ref } from 'vue'
import type { Meta, StoryObj } from '@storybook/vue3-vite'
import PwCheckbox from './PwCheckbox.vue'

/**
 * A checkbox with its label built in, because the two are never useful apart.
 *
 * The whole row is the hit target rather than the 16-pixel box, and the help text sits under
 * the label at the same indent, so a group of options reads as a list instead of a grid.
 */
const meta = {
  title: 'Forms/Checkbox',
  component: PwCheckbox,
  tags: ['autodocs'],
  args: { label: 'Flatten', disabled: false },
  render: (args) => ({
    components: { PwCheckbox },
    setup: () => ({ args, model: ref(false) }),
    template: '<PwCheckbox v-bind="args" v-model="model" />',
  }),
} satisfies Meta<typeof PwCheckbox>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = {}

/** Help text explains the consequence, which for a destructive option is the whole story. */
export const WithHelp: Story = {
  args: { label: 'Flatten', help: 'Bakes values in and removes the form. This cannot be undone.' },
}

export const Checked: Story = {
  render: (args) => ({
    components: { PwCheckbox },
    setup: () => ({ args, model: ref(true) }),
    template: '<PwCheckbox v-bind="args" v-model="model" />',
  }),
}

export const Disabled: Story = { args: { disabled: true } }

/** How a set reads together: labels aligned, help hanging beneath, one rhythm down the column. */
export const Group: Story = {
  render: () => ({
    components: { PwCheckbox },
    setup: () => ({ required: ref(true), readOnly: ref(false), multiline: ref(false) }),
    template: `
      <div style="max-width:380px;display:flex;flex-direction:column;gap:12px">
        <PwCheckbox v-model="required" label="Required" help="The reader will not submit without it" />
        <PwCheckbox v-model="readOnly" label="Read only" />
        <PwCheckbox v-model="multiline" label="Multiline" />
      </div>
    `,
  }),
}
