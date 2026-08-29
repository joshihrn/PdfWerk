import type { Meta, StoryObj } from '@storybook/vue3-vite'

/**
 * The tokens everything else is built from.
 *
 * These stories read the values live out of the document rather than restating them, so they
 * cannot drift from `tokens.css` the way a hand-written table would. Switch the theme in the
 * toolbar and every swatch here follows, which is the quickest way to see whether a change to
 * the palette holds up in both.
 */
const meta = {
  title: 'Foundations/Tokens',
  parameters: {
    // Swatch grids are colour by definition; the contrast rules that matter are checked on the
    // components themselves, where text actually sits on these backgrounds.
    a11y: { test: 'off' },
  },
} satisfies Meta

export default meta
type Story = StoryObj<typeof meta>

/** Reads a custom property off the root, so what is shown is what is actually in effect. */
function resolve(name: string): string {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim()
}

const swatchGrid = (title: string, names: string[]) => ({
  setup: () => ({ names, title, resolve }),
  template: `
    <section>
      <h3 style="font-size:13px;font-weight:600;margin-bottom:12px;color:var(--fg)">{{ title }}</h3>
      <div style="display:grid;grid-template-columns:repeat(auto-fill,minmax(150px,1fr));gap:8px">
        <div v-for="name in names" :key="name"
             style="border:1px solid var(--border);border-radius:var(--r-md);overflow:hidden;background:var(--bg-raised)">
          <div :style="{ height: '44px', background: 'var(' + name + ')', borderBottom: '1px solid var(--border)' }"></div>
          <div style="padding:6px 8px">
            <code style="font-size:11px;color:var(--fg)">{{ name }}</code>
            <div style="font-size:10px;color:var(--fg-subtle);font-family:var(--mono)">{{ resolve(name) }}</div>
          </div>
        </div>
      </div>
    </section>
  `,
})

/** The near-neutral ramp the interface is mostly made of. */
export const Neutrals: Story = {
  render: () =>
    swatchGrid('Neutral ramp', [
      '--n-0', '--n-25', '--n-50', '--n-100', '--n-200', '--n-300',
      '--n-400', '--n-500', '--n-600', '--n-700', '--n-800', '--n-900', '--n-950',
    ]),
}

/**
 * One accent, used sparingly. A palette with two accents has none, because the second one takes
 * the emphasis the first was carrying.
 */
export const Accent: Story = {
  render: () =>
    swatchGrid('Accent', ['--a-50', '--a-100', '--a-200', '--a-400', '--a-500', '--a-600', '--a-700']),
}

/** Each status carries a foreground, a background and a border, so it works as a filled block. */
export const Status: Story = {
  render: () => ({
    setup: () => ({
      groups: [
        { name: 'ok', label: 'Success' },
        { name: 'warn', label: 'Warning' },
        { name: 'bad', label: 'Error' },
      ],
    }),
    template: `
      <div style="display:flex;flex-direction:column;gap:12px;max-width:520px">
        <div v-for="g in groups" :key="g.name"
             :style="{
               background: 'var(--' + g.name + '-bg)',
               color: 'var(--' + g.name + '-fg)',
               border: '1px solid var(--' + g.name + '-bd)',
               borderRadius: 'var(--r-md)',
               padding: '12px 14px',
               fontSize: '13px',
             }">
          <strong>{{ g.label }}</strong>
          <code style="margin-left:8px;font-size:11px;opacity:0.8">
            --{{ g.name }}-fg / --{{ g.name }}-bg / --{{ g.name }}-bd
          </code>
        </div>
      </div>
    `,
  }),
}

/** The semantic layer. Components reference these, never the raw ramp above. */
export const Semantic: Story = {
  render: () =>
    swatchGrid('Surfaces and borders', [
      '--bg', '--bg-raised', '--bg-sunken', '--bg-hover', '--bg-active', '--bg-field',
      '--border', '--border-strong', '--border-field',
    ]),
}

/** Every step of the type scale, at the weight and tracking it is meant to be set in. */
export const Typography: Story = {
  render: () => ({
    setup: () => ({
      steps: [
        { token: '--t-40', use: 'Hero', weight: 'var(--w-semi)', track: 'var(--track-tight)' },
        { token: '--t-32', use: 'Display', weight: 'var(--w-semi)', track: 'var(--track-tight)' },
        { token: '--t-24', use: 'Page heading', weight: 'var(--w-semi)', track: 'var(--track-snug)' },
        { token: '--t-20', use: 'Section heading', weight: 'var(--w-semi)', track: 'var(--track-snug)' },
        { token: '--t-16', use: 'Lead', weight: 'var(--w-regular)', track: 'normal' },
        { token: '--t-14', use: 'Body', weight: 'var(--w-regular)', track: 'normal' },
        { token: '--t-13', use: 'Controls, dense body', weight: 'var(--w-regular)', track: 'normal' },
        { token: '--t-12', use: 'Meta, captions', weight: 'var(--w-regular)', track: 'normal' },
        { token: '--t-11', use: 'Labels, table headers', weight: 'var(--w-medium)', track: 'var(--track-wide)' },
      ],
    }),
    template: `
      <div style="display:flex;flex-direction:column;gap:14px;max-width:720px">
        <div v-for="s in steps" :key="s.token"
             style="display:flex;align-items:baseline;gap:16px;padding-bottom:12px;border-bottom:1px solid var(--border)">
          <code style="width:70px;flex:none;font-size:11px;color:var(--fg-subtle)">{{ s.token }}</code>
          <div :style="{ fontSize: 'var(' + s.token + ')', fontWeight: s.weight, letterSpacing: s.track, color: 'var(--fg)' }">
            Merge form fields
          </div>
          <div style="margin-left:auto;font-size:11px;color:var(--fg-subtle);flex:none">{{ s.use }}</div>
        </div>
      </div>
    `,
  }),
}

/**
 * Depth without gradients: a border, a one-pixel light edge along the top, and a contact shadow.
 * The three together are what separate a control that looks moulded from one that looks drawn.
 */
export const Elevation: Story = {
  render: () => ({
    setup: () => ({ levels: ['--shadow-xs', '--shadow-sm', '--shadow-md', '--shadow-lg'] }),
    template: `
      <div style="display:flex;gap:24px;flex-wrap:wrap;padding:8px">
        <div v-for="level in levels" :key="level"
             :style="{
               width: '128px',
               height: '84px',
               background: 'var(--bg-raised)',
               border: '1px solid var(--border)',
               borderRadius: 'var(--r-lg)',
               boxShadow: 'var(' + level + ')',
               display: 'flex',
               alignItems: 'center',
               justifyContent: 'center',
               fontSize: '11px',
               color: 'var(--fg-muted)',
             }">
          <code>{{ level }}</code>
        </div>
      </div>
    `,
  }),
}

/** Spacing is a four-pixel grid. Anything off it reads as a mistake once the page is dense. */
export const Spacing: Story = {
  render: () => ({
    setup: () => ({
      steps: ['--s-1', '--s-2', '--s-3', '--s-4', '--s-5', '--s-6', '--s-8', '--s-10', '--s-12', '--s-16'],
    }),
    template: `
      <div style="display:flex;flex-direction:column;gap:8px">
        <div v-for="step in steps" :key="step" style="display:flex;align-items:center;gap:12px">
          <code style="width:56px;font-size:11px;color:var(--fg-subtle)">{{ step }}</code>
          <div :style="{ height: '14px', width: 'var(' + step + ')', background: 'var(--a-400)', borderRadius: '2px' }"></div>
        </div>
      </div>
    `,
  }),
}

/** Three radii, and control heights that make a row of mixed controls line up. */
export const ShapeAndSize: Story = {
  render: () => ({
    setup: () => ({
      radii: ['--r-sm', '--r-md', '--r-lg'],
      heights: ['--control-h', '--control-h-lg'],
    }),
    template: `
      <div style="display:flex;flex-direction:column;gap:28px">
        <section>
          <h3 style="font-size:13px;font-weight:600;margin-bottom:12px">Radii</h3>
          <div style="display:flex;gap:16px">
            <div v-for="r in radii" :key="r"
                 :style="{
                   width: '80px', height: '80px',
                   background: 'var(--bg-raised)',
                   border: '1px solid var(--border-strong)',
                   borderRadius: 'var(' + r + ')',
                   display: 'flex', alignItems: 'center', justifyContent: 'center',
                   fontSize: '11px', color: 'var(--fg-muted)',
                 }">
              <code>{{ r }}</code>
            </div>
          </div>
        </section>

        <section>
          <h3 style="font-size:13px;font-weight:600;margin-bottom:12px">Control heights</h3>
          <div style="display:flex;gap:16px;align-items:flex-end">
            <div v-for="h in heights" :key="h"
                 :style="{
                   height: 'var(' + h + ')', padding: '0 14px',
                   background: 'var(--bg-raised)',
                   border: '1px solid var(--border-strong)',
                   borderRadius: 'var(--r-md)',
                   display: 'flex', alignItems: 'center',
                   fontSize: '11px', color: 'var(--fg-muted)',
                 }">
              <code>{{ h }}</code>
            </div>
          </div>
        </section>
      </div>
    `,
  }),
}
