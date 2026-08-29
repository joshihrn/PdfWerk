import type { Meta, StoryObj } from '@storybook/vue3-vite'
import PwBadge from './PwBadge.vue'

/**
 * A small, non-interactive label: a tier, a status, an HTTP method.
 *
 * Deliberately quiet. A badge that shouts competes with the content it is annotating, and an
 * interface where everything is emphasised has no emphasis at all.
 */
const meta = {
  title: 'Data/Badge',
  component: PwBadge,
  tags: ['autodocs'],
  argTypes: { tone: { control: 'inline-radio', options: ['neutral', 'accent', 'ok', 'warn', 'bad'] } },
  args: { tone: 'neutral', dot: false, mono: false },
  render: (args) => ({
    components: { PwBadge },
    setup: () => ({ args }),
    template: '<PwBadge v-bind="args">Free</PwBadge>',
  }),
} satisfies Meta<typeof PwBadge>

export default meta
type Story = StoryObj<typeof meta>

export const Neutral: Story = {}

export const Accent: Story = { args: { tone: 'accent' } }

/**
 * The dot gives a second, non-colour cue. Necessary the moment a badge means status rather
 * than category, because roughly one man in twelve cannot rely on the hue alone.
 */
export const WithDot: Story = { args: { tone: 'ok', dot: true } }

/** Monospaced for HTTP methods and other things that line up in a column. */
export const Monospace: Story = {
  args: { mono: true, tone: 'accent' },
  render: (args) => ({
    components: { PwBadge },
    setup: () => ({ args }),
    template: '<PwBadge v-bind="args">POST</PwBadge>',
  }),
}

export const EveryTone: Story = {
  render: () => ({
    components: { PwBadge },
    setup: () => ({ tones: ['neutral', 'accent', 'ok', 'warn', 'bad'] }),
    template: `
      <div style="display:flex;flex-direction:column;gap:12px">
        <div style="display:flex;gap:8px;flex-wrap:wrap">
          <PwBadge v-for="tone in tones" :key="tone" :tone="tone">{{ tone }}</PwBadge>
        </div>
        <div style="display:flex;gap:8px;flex-wrap:wrap">
          <PwBadge v-for="tone in tones" :key="tone" :tone="tone" dot>{{ tone }}</PwBadge>
        </div>
      </div>
    `,
  }),
}
