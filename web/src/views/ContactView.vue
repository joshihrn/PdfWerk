<script setup lang="ts">
/**
 * The contact form.
 *
 * Asks the server whether it can send before showing the form, so nobody writes three paragraphs
 * and only then finds out this instance has no mail provider configured — which is the normal
 * state for a self-hosted copy, and would otherwise be a dead end with no explanation.
 */
import { onMounted, ref } from 'vue'
import { PwButton, PwCallout, PwCard, PwField, PwInput, PwPageHeader, PwTextarea } from '../components/ui'

const name = ref('')
const email = ref('')
const message = ref('')

/**
 * The honeypot.
 *
 * Hidden from sight and from assistive technology, and taken out of the tab order, so no person
 * ever reaches it. Scripts that fill every input they find will, and the server treats a filled
 * one as spam.
 */
const website = ref('')

const configured = ref<boolean | null>(null)
const busy = ref(false)
const sent = ref(false)
const error = ref<string | null>(null)

const ready = () => name.value.trim() && email.value.trim() && message.value.trim().length >= 10

onMounted(async () => {
  try {
    configured.value = (await (await fetch('/v1/contact')).json()).configured
  } catch {
    // Treated as available: better to let someone try and see a real error than to hide the form
    // because one status check failed.
    configured.value = true
  }
})

async function send() {
  if (!ready() || busy.value) return

  busy.value = true
  error.value = null

  try {
    const response = await fetch('/v1/contact', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        name: name.value,
        email: email.value,
        message: message.value,
        website: website.value,
      }),
    })

    if (!response.ok) {
      const body = await response.json().catch(() => ({}))
      throw new Error(body.message ?? 'That message could not be sent.')
    }

    sent.value = true
    name.value = ''
    email.value = ''
    message.value = ''
  } catch (ex) {
    error.value = ex instanceof Error ? ex.message : String(ex)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="contact">
    <PwPageHeader
      title="Get in touch"
      description="Questions about the API, a bug, a licensing conversation, or something that should work and does not."
    />

    <PwCallout v-if="configured === false" tone="warn" title="This instance cannot send mail">
      No mail provider is configured here, so the form below would go nowhere. Open an issue on
      <a href="https://github.com/joshihrn/PdfWerk/issues" target="_blank" rel="noopener">GitHub</a>
      instead — it is a better place for anything technical anyway.
    </PwCallout>

    <div v-else class="split">
      <PwCard title="Send a message">
        <PwCallout v-if="sent" tone="ok" title="Message sent">
          Thanks — it is on its way, and a reply will come to the address you gave.
        </PwCallout>

        <template v-else>
          <PwCallout v-if="error" tone="bad" assertive title="That did not send" class="mb">
            {{ error }}
          </PwCallout>

          <div class="stack-4">
            <PwField v-slot="{ id }" label="Your name" required>
              <PwInput :id="id" v-model="name" autocomplete="name" placeholder="Ada Lovelace" />
            </PwField>

            <PwField v-slot="{ id }" label="Your email" required help="Only used to reply to you">
              <PwInput :id="id" v-model="email" type="email" autocomplete="email" placeholder="ada@example.com" />
            </PwField>

            <PwField v-slot="{ id }" label="Message" required>
              <PwTextarea
                :id="id"
                v-model="message"
                :rows="7"
                :mono="false"
                placeholder="What can we help with?"
              />
            </PwField>

            <!--
              The honeypot. aria-hidden and tabindex="-1" keep it away from screen readers and the
              keyboard; it is moved off-screen rather than display:none because some bots skip
              fields that are not rendered at all.
            -->
            <div class="trap" aria-hidden="true">
              <label for="website">Website</label>
              <input id="website" v-model="website" type="text" tabindex="-1" autocomplete="off" />
            </div>
          </div>
        </template>

        <template v-if="!sent" #footer>
          <PwButton variant="solid" :loading="busy" :disabled="!ready()" @click="send">
            Send message
          </PwButton>
          <span v-if="!ready()" class="t-12 subtle">
            Name, email and a few words are all that is needed
          </span>
        </template>
      </PwCard>

      <div class="stack-4">
        <PwCard title="Faster elsewhere">
          <p class="t-13 subtle">
            For a bug or a feature request, an issue on
            <a href="https://github.com/joshihrn/PdfWerk/issues" target="_blank" rel="noopener">GitHub</a>
            gets a better answer than an email — it can carry a stack trace, and other people can
            find it afterwards.
          </p>
        </PwCard>

        <PwCard title="Before you write">
          <ul class="t-13 subtle list">
            <li>
              Rate limits and quotas are explained on the
              <RouterLink to="/api">API page</RouterLink>.
            </li>
            <li>
              What happens to an uploaded document is set out in the
              <RouterLink to="/privacy">privacy notice</RouterLink>.
            </li>
            <li>
              Licensing, including what you may do commercially, is in
              <a href="https://github.com/joshihrn/PdfWerk/blob/main/LICENSING.md" target="_blank" rel="noopener">LICENSING.md</a>.
            </li>
          </ul>
        </PwCard>
      </div>
    </div>
  </div>
</template>

<style scoped>
.mb { margin-bottom: var(--s-4); }

.list { margin: 0; padding-left: var(--s-5); }
.list li { margin-bottom: var(--s-2); }

/*
 * Off-screen rather than hidden. A field with display:none is skipped by the more careful bots,
 * which defeats the purpose; one positioned out of the viewport is still "there" to a script that
 * walks the DOM, and unreachable to everyone else.
 */
.trap {
  position: absolute;
  left: -9999px;
  width: 1px;
  height: 1px;
  overflow: hidden;
}
</style>
