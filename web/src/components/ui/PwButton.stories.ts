import type { Meta, StoryObj } from '@storybook/vue3-vite'
import PwButton from './PwButton.vue'

/**
 * The one button. Variants carry meaning rather than decoration: `solid` is the single primary
 * action on a screen, `outline` is everything else, `ghost` recedes into a toolbar, `danger` is
 * for things that cannot be undone.
 */
const meta = {
  title: 'Controls/Button',
  component: PwButton,
  tags: ['autodocs'],
  argTypes: {
    variant: { control: 'select', options: ['solid', 'outline', 'ghost', 'danger', 'accent'] },
    size: { control: 'inline-radio', options: ['sm', 'md', 'lg'] },
    onClick: { action: 'click' },
  },
  args: { variant: 'outline', size: 'md', disabled: false, loading: false, block: false },
  render: (args) => ({
    components: { PwButton },
    setup: () => ({ args }),
    template: '<PwButton v-bind="args">Generate PDF</PwButton>',
  }),
} satisfies Meta<typeof PwButton>

export default meta
type Story = StoryObj<typeof meta>

export const Outline: Story = {}

/** One per screen. Two primary actions side by side means neither is primary. */
export const Solid: Story = { args: { variant: 'solid' } }

export const Ghost: Story = { args: { variant: 'ghost' } }

/** Reserved for the irreversible: revoking a key, deleting a document. */
export const Danger: Story = {
  args: { variant: 'danger' },
  render: (args) => ({
    components: { PwButton },
    setup: () => ({ args }),
    template: '<PwButton v-bind="args">Revoke permanently</PwButton>',
  }),
}

export const Accent: Story = { args: { variant: 'accent' } }

/**
 * Loading keeps the label in place and adds a spinner, so the button does not resize and the
 * pointer does not lose its target mid-click.
 */
export const Loading: Story = { args: { variant: 'solid', loading: true } }

export const Disabled: Story = { args: { disabled: true } }

/** Full width, for the foot of a narrow panel or a phone-width layout. */
export const Block: Story = { args: { variant: 'solid', block: true } }

/**
 * Renders an anchor when given `href`, so a link stays a link: keyboard behaviour, middle-click
 * and "open in new tab" all keep working, which a button styled as a link throws away.
 */
export const AsLink: Story = {
  args: { href: '/docs' },
  render: (args) => ({
    components: { PwButton },
    setup: () => ({ args }),
    template: '<PwButton v-bind="args">Read the API reference</PwButton>',
  }),
}

/** Every size against every variant. The view to check when changing a shared token. */
export const Matrix: Story = {
  render: () => ({
    components: { PwButton },
    setup: () => ({
      variants: ['solid', 'outline', 'ghost', 'accent', 'danger'],
      sizes: ['sm', 'md', 'lg'],
    }),
    template: `
      <div style="display:flex;flex-direction:column;gap:16px">
        <div v-for="size in sizes" :key="size" style="display:flex;gap:8px;align-items:center;flex-wrap:wrap">
          <code style="width:32px;font-size:12px;color:var(--fg-subtle)">{{ size }}</code>
          <PwButton v-for="v in variants" :key="v" :variant="v" :size="size">{{ v }}</PwButton>
        </div>
      </div>
    `,
  }),
}
