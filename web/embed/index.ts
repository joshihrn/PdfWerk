/**
 * PdfWerk embeddable widget.
 *
 * A single script tag that renders a working PDF tool inside any page — React, Rails, WordPress,
 * plain HTML — and hands back the finished document.
 *
 * Deliberately dependency-free and rendered into a shadow root. An embed lands in a page whose
 * framework, bundler and CSS are unknown and not ours to break: shipping a framework would risk
 * a version clash, and rendering into the light DOM would let the host's stylesheet reshape the
 * widget (or ours reshape theirs). Shadow DOM makes both impossible.
 */

type Tool = 'create' | 'word' | 'merge' | 'summarize' | 'fill' | 'inspect' | 'split' | 'rotate' | 'watermark'

type DeliveryMode = 'download' | 'preview' | 'callback'

export interface MountOptions {
  /** Which tool to render. */
  tool: Tool

  /** Optional API key. Raises the caller's quota; omitted means the anonymous tier. */
  apiKey?: string

  /** Where the API lives. Defaults to the origin serving this script. */
  baseUrl?: string

  /**
   * What to do with the finished document.
   * - `download` saves it, `preview` renders it inline, `callback` hands it to onResult only.
   */
  delivery?: DeliveryMode

  /** Called with the finished document. Always fires, whatever the delivery mode. */
  onResult?: (blob: Blob, meta: { fileName: string; contentType: string }) => void

  /** Called for JSON-returning tools (summarize, inspect). */
  onData?: (data: unknown) => void

  onError?: (error: Error) => void

  /** Colour scheme. `auto` follows the host page's prefers-color-scheme. */
  theme?: 'light' | 'dark' | 'auto'

  /** Replaces the default heading. Pass an empty string to hide it. */
  title?: string

  /** Seeds the editor for the `create` tool. */
  initialContent?: string
}

export interface WidgetHandle {
  /** Removes the widget and its listeners from the page. */
  destroy(): void
}

const STYLES = `
:host { all: initial; }
* { box-sizing: border-box; }
.pw {
  font: 14px/1.5 ui-sans-serif, system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
  color: var(--pw-ink);
  background: var(--pw-bg);
  border: 1px solid var(--pw-line);
  border-radius: 12px;
  padding: 16px;
}
.pw h3 { margin: 0 0 4px; font-size: 15px; font-weight: 650; }
.pw .sub { margin: 0 0 14px; font-size: 12.5px; color: var(--pw-muted); }
.pw label { display: block; font-size: 12px; color: var(--pw-muted); margin: 10px 0 4px; }
.pw textarea, .pw input, .pw select {
  width: 100%; font: inherit; padding: 8px 10px; border-radius: 8px;
  border: 1px solid var(--pw-line); background: var(--pw-field); color: var(--pw-ink);
}
.pw textarea { min-height: 130px; resize: vertical; font-family: ui-monospace, Menlo, monospace; font-size: 12.5px; }
.pw input:focus, .pw textarea:focus, .pw select:focus { outline: none; border-color: var(--pw-accent); }
.pw .row { display: flex; gap: 10px; flex-wrap: wrap; }
.pw .row > * { flex: 1 1 140px; }
.pw .btns { display: flex; gap: 8px; margin-top: 14px; flex-wrap: wrap; }
.pw button {
  font: inherit; font-weight: 600; padding: 8px 14px; border-radius: 8px; cursor: pointer;
  border: 1px solid var(--pw-line); background: var(--pw-field); color: var(--pw-ink);
}
.pw button.primary { background: var(--pw-accent); border-color: transparent; color: #fff; }
.pw button:disabled { opacity: .55; cursor: not-allowed; }
.pw .drop {
  border: 1.5px dashed var(--pw-line); border-radius: 10px; padding: 20px;
  text-align: center; color: var(--pw-muted); cursor: pointer;
}
.pw .drop.over { border-color: var(--pw-accent); color: var(--pw-ink); }
.pw .drop input { display: none; }
.pw .files { margin-top: 8px; font-size: 12.5px; color: var(--pw-muted); }
.pw .msg { margin-top: 12px; font-size: 12.5px; padding: 9px 11px; border-radius: 8px; border: 1px solid; }
.pw .msg.ok { color: #17795a; border-color: #9ad9c2; background: #eefaf5; }
.pw .msg.err { color: #a32c2c; border-color: #eab3b3; background: #fdefef; }
.pw .msg.info { color: var(--pw-muted); border-color: var(--pw-line); background: var(--pw-field); }
.pw iframe { width: 100%; height: 420px; border: 1px solid var(--pw-line); border-radius: 8px; margin-top: 12px; background: #fff; }
.pw pre { white-space: pre-wrap; word-break: break-word; font-size: 12.5px; margin: 10px 0 0; color: var(--pw-muted); }
.pw .foot { margin-top: 12px; font-size: 11px; color: var(--pw-muted); text-align: right; }
.pw .foot a { color: var(--pw-muted); }
`

const LIGHT = `
:host { --pw-bg:#fff; --pw-field:#f7f8fb; --pw-line:#dde2ec; --pw-ink:#111827; --pw-muted:#6b7280; --pw-accent:#2d6cdf; }
`

const DARK = `
:host { --pw-bg:#131a2e; --pw-field:#0e1428; --pw-line:#27314f; --pw-ink:#e8ecf7; --pw-muted:#97a2c0; --pw-accent:#2d6cdf; }
.pw .msg.ok { color:#7ce0bd; border-color:#1f6f52; background:#10241d; }
.pw .msg.err { color:#ff9a9a; border-color:#6f2b2b; background:#241111; }
`

/** Where this script was served from, used as the default API origin. */
function defaultBaseUrl(): string {
  const current = document.currentScript as HTMLScriptElement | null
  if (current?.src) {
    try {
      return new URL(current.src).origin
    } catch {
      // A malformed src is not worth failing over; fall through to the page origin.
    }
  }

  return window.location.origin
}

const scriptOrigin = defaultBaseUrl()

interface ToolSpec {
  title: string
  subtitle: string
  /** Renders the tool's own controls and returns a submit handler. */
  build: (ui: WidgetUi) => void
}

class WidgetUi {
  readonly root: ShadowRoot
  readonly body: HTMLDivElement
  private readonly options: MountOptions
  private readonly base: string
  private message: HTMLDivElement | null = null
  private previewUrl: string | null = null

  constructor(host: HTMLElement, options: MountOptions) {
    this.options = options
    this.base = (options.baseUrl ?? scriptOrigin).replace(/\/$/, '')

    this.root = host.attachShadow({ mode: 'open' })

    const style = document.createElement('style')
    const theme = options.theme ?? 'auto'
    const dark =
      theme === 'dark' ||
      (theme === 'auto' && window.matchMedia?.('(prefers-color-scheme: dark)').matches)

    style.textContent = (dark ? DARK : LIGHT) + STYLES
    this.root.appendChild(style)

    this.body = document.createElement('div')
    this.body.className = 'pw'
    this.root.appendChild(this.body)
  }

  get apiBase() {
    return this.base
  }

  el<K extends keyof HTMLElementTagNameMap>(
    tag: K,
    props: Partial<HTMLElementTagNameMap[K]> = {},
    text?: string,
  ): HTMLElementTagNameMap[K] {
    const node = document.createElement(tag)
    Object.assign(node, props)
    if (text !== undefined) node.textContent = text
    this.body.appendChild(node)
    return node
  }

  add<T extends HTMLElement>(node: T): T {
    this.body.appendChild(node)
    return node
  }

  label(text: string) {
    const node = document.createElement('label')
    node.textContent = text
    this.body.appendChild(node)
    return node
  }

  say(text: string, kind: 'ok' | 'err' | 'info' = 'info') {
    if (!this.message) {
      this.message = document.createElement('div')
      this.body.appendChild(this.message)
    }

    this.message.className = `msg ${kind}`
    this.message.textContent = text
  }

  clearMessage() {
    if (this.message) this.message.textContent = ''
  }

  /** Sends a request that returns a document, honouring the configured delivery mode. */
  async submitDocument(path: string, init: RequestInit, fallbackName: string) {
    const headers = new Headers(init.headers)
    if (this.options.apiKey) headers.set('X-Api-Key', this.options.apiKey)

    // 'preview' and 'callback' both need the bytes inline rather than as an attachment.
    const wire = this.options.delivery === 'download' ? 'download' : 'stream'

    const response = await fetch(`${this.base}${path}?delivery=${wire}`, { ...init, headers })

    if (!response.ok) throw await this.toError(response)

    const blob = await response.blob()
    const fileName = this.fileNameOf(response, fallbackName)

    this.options.onResult?.(blob, { fileName, contentType: blob.type })

    const mode = this.options.delivery ?? 'download'
    if (mode === 'download') this.save(blob, fileName)
    else if (mode === 'preview') this.preview(blob)

    const remaining = response.headers.get('X-RateLimit-Remaining')
    const limit = response.headers.get('X-RateLimit-Limit')

    this.say(
      `${fileName} · ${(blob.size / 1024).toFixed(1)} KB` +
        (remaining ? ` · ${remaining}/${limit} left` : ''),
      'ok',
    )
  }

  async submitJson(path: string, init: RequestInit) {
    const headers = new Headers(init.headers)
    if (this.options.apiKey) headers.set('X-Api-Key', this.options.apiKey)

    const response = await fetch(`${this.base}${path}`, { ...init, headers })
    if (!response.ok) throw await this.toError(response)

    const data = await response.json()
    this.options.onData?.(data)
    return data
  }

  private async toError(response: Response) {
    let message = `Request failed (${response.status})`
    try {
      const body = await response.json()
      message = body.message ?? message
    } catch {
      // Non-JSON error body; the status stands.
    }

    return new Error(message)
  }

  private fileNameOf(response: Response, fallback: string) {
    const disposition = response.headers.get('Content-Disposition') ?? ''
    const match = /filename\*=UTF-8''([^;]+)/i.exec(disposition) ?? /filename="?([^";]+)"?/i.exec(disposition)
    return match ? decodeURIComponent(match[1]) : fallback
  }

  private save(blob: Blob, fileName: string) {
    const url = URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = fileName
    anchor.click()
    setTimeout(() => URL.revokeObjectURL(url), 1000)
  }

  preview(blob: Blob) {
    if (this.previewUrl) URL.revokeObjectURL(this.previewUrl)
    this.previewUrl = URL.createObjectURL(blob)

    let frame = this.body.querySelector('iframe')
    if (!frame) {
      frame = document.createElement('iframe')
      this.body.appendChild(frame)
    }

    frame.src = this.previewUrl
  }

  fail(error: unknown) {
    const wrapped = error instanceof Error ? error : new Error(String(error))
    this.say(wrapped.message, 'err')
    this.options.onError?.(wrapped)
  }

  /** Builds a file picker that also accepts drops. */
  filePicker(accept: string, multiple: boolean, label: string) {
    const drop = document.createElement('div')
    drop.className = 'drop'
    drop.textContent = label

    const input = document.createElement('input')
    input.type = 'file'
    input.accept = accept
    input.multiple = multiple
    drop.appendChild(input)

    const list = document.createElement('div')
    list.className = 'files'

    const state: { files: File[] } = { files: [] }

    const update = (files: FileList | null) => {
      if (!files?.length) return
      state.files = multiple ? Array.from(files) : [files[0]]
      list.textContent = state.files.map((f) => f.name).join(', ')
    }

    drop.addEventListener('click', () => input.click())
    input.addEventListener('change', () => update(input.files))
    drop.addEventListener('dragover', (e) => {
      e.preventDefault()
      drop.classList.add('over')
    })
    drop.addEventListener('dragleave', () => drop.classList.remove('over'))
    drop.addEventListener('drop', (e) => {
      e.preventDefault()
      drop.classList.remove('over')
      update(e.dataTransfer?.files ?? null)
    })

    this.body.appendChild(drop)
    this.body.appendChild(list)
    return state
  }

  buttons(...specs: { text: string; primary?: boolean; run: () => Promise<void> }[]) {
    const bar = document.createElement('div')
    bar.className = 'btns'

    const all: HTMLButtonElement[] = []

    for (const spec of specs) {
      const button = document.createElement('button')
      button.textContent = spec.text
      if (spec.primary) button.className = 'primary'

      button.addEventListener('click', async () => {
        all.forEach((b) => (b.disabled = true))
        this.say('Working…', 'info')

        try {
          await spec.run()
        } catch (error) {
          this.fail(error)
        } finally {
          all.forEach((b) => (b.disabled = false))
        }
      })

      all.push(button)
      bar.appendChild(button)
    }

    this.body.appendChild(bar)
  }

  dispose() {
    if (this.previewUrl) URL.revokeObjectURL(this.previewUrl)
  }
}

const TOOLS: Record<Tool, ToolSpec> = {
  create: {
    title: 'Create a PDF',
    subtitle: 'Write Markdown or plain text and get a paginated document.',
    build: (ui) => {
      ui.label('Content')
      const content = ui.el('textarea')
      content.value = '# Hello\n\nWrite **Markdown** here.'

      ui.label('Title')
      const title = ui.el('input', { type: 'text', placeholder: 'Document title' })

      ui.buttons({
        text: 'Create PDF',
        primary: true,
        run: () =>
          ui.submitDocument(
            '/v1/create/text',
            {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ content: content.value, title: title.value || null, format: 'Markdown' }),
            },
            'document.pdf',
          ),
      })
    },
  },

  word: {
    title: 'Word to PDF',
    subtitle: 'Convert a .docx document.',
    build: (ui) => {
      const picked = ui.filePicker('.docx,.doc', false, 'Drop a Word document, or click to choose')

      ui.buttons({
        text: 'Convert',
        primary: true,
        run: async () => {
          if (!picked.files[0]) throw new Error('Choose a document first.')

          const form = new FormData()
          form.append('file', picked.files[0])
          await ui.submitDocument('/v1/create/word', { method: 'POST', body: form }, 'converted.pdf')
        },
      })
    },
  },

  merge: {
    title: 'Merge PDFs',
    subtitle: 'Combine documents in the order chosen.',
    build: (ui) => {
      const picked = ui.filePicker('application/pdf', true, 'Drop PDFs, or click to choose')

      ui.buttons({
        text: 'Merge',
        primary: true,
        run: async () => {
          if (picked.files.length < 2) throw new Error('Choose at least two PDFs.')

          const form = new FormData()
          picked.files.forEach((f) => form.append('files', f))
          await ui.submitDocument('/v1/merge', { method: 'POST', body: form }, 'merged.pdf')
        },
      })
    },
  },

  summarize: {
    title: 'Summarise a PDF',
    subtitle: 'Extracts the text and returns an AI summary.',
    build: (ui) => {
      const picked = ui.filePicker('application/pdf', false, 'Drop a PDF, or click to choose')

      ui.label('Style')
      const style = ui.el('select')
      for (const option of ['Brief', 'Detailed', 'Bullets', 'ExecutiveSummary']) {
        style.appendChild(new Option(option, option))
      }

      const output = ui.el('pre')

      ui.buttons({
        text: 'Summarise',
        primary: true,
        run: async () => {
          if (!picked.files[0]) throw new Error('Choose a PDF first.')

          const form = new FormData()
          form.append('file', picked.files[0])
          form.append('request', JSON.stringify({ style: style.value, maxWords: 250 }))

          const data = (await ui.submitJson('/v1/summarize', { method: 'POST', body: form })) as {
            summary: string
            keyPoints: string[]
            providerUsed: string
          }

          output.textContent =
            data.summary + (data.keyPoints?.length ? '\n\n• ' + data.keyPoints.join('\n• ') : '')

          ui.say(`Summarised via ${data.providerUsed}`, 'ok')
        },
      })
    },
  },

  fill: {
    title: 'Fill a PDF form',
    subtitle: 'Reads the form fields, then merges your values in.',
    build: (ui) => {
      const picked = ui.filePicker('application/pdf', false, 'Drop a form PDF, or click to choose')

      const fieldHost = document.createElement('div')
      ui.add(fieldHost)

      const inputs = new Map<string, HTMLInputElement | HTMLSelectElement>()

      ui.buttons(
        {
          text: 'Read fields',
          run: async () => {
            if (!picked.files[0]) throw new Error('Choose a PDF first.')

            const form = new FormData()
            form.append('file', picked.files[0])

            const info = (await ui.submitJson('/v1/inspect', { method: 'POST', body: form })) as {
              fields: { name: string; type: string; options: string[] }[]
            }

            fieldHost.textContent = ''
            inputs.clear()

            if (!info.fields.length) {
              ui.say('This PDF has no form fields.', 'err')
              return
            }

            for (const field of info.fields) {
              const label = document.createElement('label')
              label.textContent = `${field.name} · ${field.type}`
              fieldHost.appendChild(label)

              // Choice and checkbox fields get a select, so only valid values can be sent.
              if (field.options?.length || field.type === 'Checkbox') {
                const select = document.createElement('select')
                const values = field.options?.length ? field.options : ['true', 'false']
                select.appendChild(new Option('—', ''))
                values.forEach((v) => select.appendChild(new Option(v, v)))
                fieldHost.appendChild(select)
                inputs.set(field.name, select)
              } else {
                const input = document.createElement('input')
                input.type = 'text'
                fieldHost.appendChild(input)
                inputs.set(field.name, input)
              }
            }

            ui.say(`${info.fields.length} field(s) found.`, 'ok')
          },
        },
        {
          text: 'Fill',
          primary: true,
          run: async () => {
            if (!picked.files[0]) throw new Error('Choose a PDF first.')
            if (inputs.size === 0) throw new Error('Read the fields first.')

            const values: Record<string, string> = {}
            inputs.forEach((input, name) => {
              if (input.value) values[name] = input.value
            })

            const form = new FormData()
            form.append('file', picked.files[0])
            form.append('request', JSON.stringify({ values, flatten: false, strictFieldNames: false }))

            await ui.submitDocument('/v1/forms/fill', { method: 'POST', body: form }, 'filled.pdf')
          },
        },
      )
    },
  },

  split: {
    title: 'Split a PDF',
    subtitle: 'Extract page ranges, or burst into single pages.',
    build: (ui) => {
      const picked = ui.filePicker('application/pdf', false, 'Drop a PDF, or click to choose')

      ui.label('Pages')
      const pages = ui.el('input', { type: 'text', placeholder: 'all, 1-3,7, odd, 5-' })
      pages.value = 'all'

      ui.label('How to split')
      const mode = ui.el('select')
      for (const [value, text] of [
        ['Extract', 'Extract — one document'],
        ['Burst', 'Burst — one per page'],
        ['Groups', 'Groups — one per range'],
      ]) {
        mode.appendChild(new Option(text, value))
      }

      ui.buttons({
        text: 'Split',
        primary: true,
        run: async () => {
          if (!picked.files[0]) throw new Error('Choose a PDF first.')

          const form = new FormData()
          form.append('file', picked.files[0])
          form.append('request', JSON.stringify({ pages: pages.value, mode: mode.value }))

          // Several outputs come back as a zip, which cannot be previewed inline.
          await ui.submitDocument('/v1/split', { method: 'POST', body: form }, 'split.zip')
        },
      })
    },
  },

  rotate: {
    title: 'Rotate pages',
    subtitle: 'Turn selected pages by a quarter turn.',
    build: (ui) => {
      const picked = ui.filePicker('application/pdf', false, 'Drop a PDF, or click to choose')

      ui.label('Pages')
      const pages = ui.el('input', { type: 'text', placeholder: 'all, 1-3, odd' })
      pages.value = 'all'

      ui.label('Rotation')
      const degrees = ui.el('select')
      for (const [value, text] of [['90', '90 clockwise'], ['180', '180'], ['270', '270 clockwise'], ['-90', '90 anticlockwise']]) {
        degrees.appendChild(new Option(text, value))
      }

      ui.buttons({
        text: 'Rotate',
        primary: true,
        run: async () => {
          if (!picked.files[0]) throw new Error('Choose a PDF first.')

          const form = new FormData()
          form.append('file', picked.files[0])
          form.append('request', JSON.stringify({ pages: pages.value, degrees: Number(degrees.value) }))

          await ui.submitDocument('/v1/rotate', { method: 'POST', body: form }, 'rotated.pdf')
        },
      })
    },
  },

  watermark: {
    title: 'Watermark a PDF',
    subtitle: 'Stamp text across the pages.',
    build: (ui) => {
      const picked = ui.filePicker('application/pdf', false, 'Drop a PDF, or click to choose')

      ui.label('Text')
      const text = ui.el('input', { type: 'text', maxLength: 200 })
      text.value = 'CONFIDENTIAL'

      ui.label('Pages')
      const pages = ui.el('input', { type: 'text', placeholder: 'all, 1-3, odd' })
      pages.value = 'all'

      ui.label('Orientation')
      const position = ui.el('select')
      for (const option of ['Diagonal', 'Horizontal', 'Vertical']) position.appendChild(new Option(option, option))

      ui.buttons({
        text: 'Apply watermark',
        primary: true,
        run: async () => {
          if (!picked.files[0]) throw new Error('Choose a PDF first.')
          if (!text.value.trim()) throw new Error('Enter the watermark text.')

          const form = new FormData()
          form.append('file', picked.files[0])
          form.append('request', JSON.stringify({
            text: text.value,
            pages: pages.value,
            position: position.value,
            opacity: 0.15,
          }))

          await ui.submitDocument('/v1/watermark', { method: 'POST', body: form }, 'watermarked.pdf')
        },
      })
    },
  },

  inspect: {
    title: 'Inspect a PDF',
    subtitle: 'Page count, metadata and form fields.',
    build: (ui) => {
      const picked = ui.filePicker('application/pdf', false, 'Drop a PDF, or click to choose')
      const output = ui.el('pre')

      ui.buttons({
        text: 'Inspect',
        primary: true,
        run: async () => {
          if (!picked.files[0]) throw new Error('Choose a PDF first.')

          const form = new FormData()
          form.append('file', picked.files[0])

          const info = (await ui.submitJson('/v1/inspect', { method: 'POST', body: form })) as Record<string, unknown>
          output.textContent = JSON.stringify(info, null, 2)
          ui.say('Done.', 'ok')
        },
      })
    },
  },
}

function mount(target: string | HTMLElement, options: MountOptions): WidgetHandle {
  const host = typeof target === 'string' ? document.querySelector<HTMLElement>(target) : target

  if (!host) throw new Error(`PdfWerk: no element matched "${String(target)}".`)

  const spec = TOOLS[options.tool]
  if (!spec) {
    throw new Error(`PdfWerk: unknown tool "${options.tool}". Available: ${Object.keys(TOOLS).join(', ')}.`)
  }

  const ui = new WidgetUi(host, options)

  const heading = options.title ?? spec.title
  if (heading) ui.el('h3', {}, heading)
  ui.el('p', { className: 'sub' }, spec.subtitle)

  spec.build(ui)

  const foot = ui.el('div', { className: 'foot' })
  foot.innerHTML = 'powered by <a href="https://pdfwerk.com" target="_blank" rel="noopener">PdfWerk</a>'

  return {
    destroy() {
      ui.dispose()
      host.replaceChildren()
      // A shadow root cannot be detached, so the host itself is replaced to shed it.
      host.replaceWith(host.cloneNode(false))
    },
  }
}

/** Tool names this build supports, so an integrator can feature-detect rather than guess. */
export const tools = Object.keys(TOOLS) as Tool[]

export const version = '1.0.0'

export { mount }

/*
 * Everything is exported by name rather than assigned to window here. The IIFE wrapper binds
 * this module's exports object to the global, so a manual window assignment would simply be
 * overwritten — which is exactly how `PdfWerk.version` came back undefined the first time.
 */
export default { mount, tools, version }
