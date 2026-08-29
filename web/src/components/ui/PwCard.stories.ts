import type { Meta, StoryObj } from '@storybook/vue3-vite'
import PwBadge from './PwBadge.vue'
import PwButton from './PwButton.vue'
import PwCard from './PwCard.vue'

/**
 * The surface everything sits on.
 *
 * Header, body and footer are separate regions with their own rules: the header holds a title
 * and at most one control, the footer holds actions. Keeping actions out of the body is what
 * stops a column of cards turning into a wall of undifferentiated buttons.
 */
const meta = {
  title: 'Surfaces/Card',
  component: PwCard,
  tags: ['autodocs'],
  args: { title: 'Your key', flush: false },
  render: (args) => ({
    components: { PwCard },
    setup: () => ({ args }),
    template: `
      <div style="max-width:480px">
        <PwCard v-bind="args">
          <p style="font-size:13px;color:var(--fg-muted)">
            A free key raises your limits and takes one request. No account, no email.
          </p>
        </PwCard>
      </div>
    `,
  }),
} satisfies Meta<typeof PwCard>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = {}

export const WithDescription: Story = {
  args: { description: 'Stored in this browser only, never sent anywhere but the API' },
}

/** One control in the header, for something that acts on the card as a whole. */
export const WithHeaderAction: Story = {
  render: (args) => ({
    components: { PwCard, PwBadge },
    setup: () => ({ args }),
    template: `
      <div style="max-width:480px">
        <PwCard v-bind="args" title="Remaining quota">
          <template #actions><PwBadge tone="ok">Free</PwBadge></template>
          <p style="font-size:13px;color:var(--fg-muted)">20 requests a minute, 500 a day.</p>
        </PwCard>
      </div>
    `,
  }),
}

/** Actions live in the footer, separated by a rule, so the body stays readable. */
export const WithFooter: Story = {
  render: (args) => ({
    components: { PwCard, PwButton },
    setup: () => ({ args }),
    template: `
      <div style="max-width:480px">
        <PwCard v-bind="args">
          <p style="font-size:13px;color:var(--fg-muted)">
            Forget clears it from this browser. Revoke disables it everywhere, permanently.
          </p>
          <template #footer>
            <PwButton size="sm">Forget it here</PwButton>
            <PwButton size="sm" variant="danger">Revoke permanently</PwButton>
          </template>
        </PwCard>
      </div>
    `,
  }),
}

/**
 * `flush` removes the body padding for content that manages its own edges: tables, previews,
 * and the PDF canvas in the form designer.
 */
export const Flush: Story = {
  args: { flush: true, title: 'Current identity' },
  render: (args) => ({
    components: { PwCard },
    setup: () => ({ args }),
    template: `
      <div style="max-width:480px">
        <PwCard v-bind="args">
          <table class="table">
            <tbody>
              <tr><th scope="row">tier</th><td>Free</td></tr>
              <tr><th scope="row">identity</th><td>key:9f2c</td></tr>
              <tr><th scope="row">limits</th><td>per action</td></tr>
            </tbody>
          </table>
        </PwCard>
      </div>
    `,
  }),
}
