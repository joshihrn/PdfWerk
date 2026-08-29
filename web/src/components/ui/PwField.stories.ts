import type { Meta, StoryObj } from '@storybook/vue3-vite'
import PwField from './PwField.vue'
import PwInput from './PwInput.vue'
import PwSelect from './PwSelect.vue'
import PwTextarea from './PwTextarea.vue'

/**
 * The wrapper that makes a control announceable.
 *
 * It generates an id, binds the label to it, and points `aria-describedby` at whichever of the
 * help and error text is present. The control receives that id through a scoped slot, so there
 * is no way to use this component and still end up with an unlabelled input. That is the point.
 */
const meta = {
  title: 'Forms/Field',
  component: PwField,
  tags: ['autodocs'],
  args: { label: 'Document title', required: false, hideLabel: false },
  render: (args) => ({
    components: { PwField, PwInput },
    setup: () => ({ args }),
    template: `
      <div style="max-width:360px">
        <PwField v-bind="args" v-slot="{ id }">
          <PwInput :id="id" placeholder="Quarterly report" />
        </PwField>
      </div>
    `,
  }),
} satisfies Meta<typeof PwField>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = {}

export const WithHelp: Story = {
  args: { help: 'Written to the PDF metadata and used as the download filename' },
}

/** The error replaces the help text rather than stacking under it, so there is one thing to read. */
export const WithError: Story = {
  args: { error: 'A title is required before the document can be generated' },
}

export const Required: Story = { args: { required: true } }

/**
 * For a control whose purpose is already clear from context, such as a search box directly
 * under a "Search" heading. The label still exists, it is simply not painted.
 */
export const HiddenLabel: Story = { args: { hideLabel: true, label: 'Search documents' } }

/** The same wrapper around every control, which is what keeps a form vertically rhythmic. */
export const EveryControl: Story = {
  render: () => ({
    components: { PwField, PwInput, PwSelect, PwTextarea },
    setup: () => ({
      formats: [
        { value: 'Markdown', label: 'Markdown' },
        { value: 'Plain', label: 'Plain text' },
        { value: 'Html', label: 'HTML' },
      ],
    }),
    template: `
      <div style="max-width:420px;display:flex;flex-direction:column;gap:16px">
        <PwField label="Title" v-slot="{ id }">
          <PwInput :id="id" placeholder="Quarterly report" />
        </PwField>
        <PwField label="Format" help="How the body is interpreted" v-slot="{ id }">
          <PwSelect :id="id" :options="formats" />
        </PwField>
        <PwField label="Document body" required v-slot="{ id }">
          <PwTextarea :id="id" :rows="5" placeholder="# Heading" />
        </PwField>
      </div>
    `,
  }),
}
