<script setup lang="ts">
/**
 * The terms of use.
 *
 * Kept short and specific. The obligations that actually matter here are narrow — do not upload
 * what you have no right to, do not route around the rate limits, and understand that a free
 * service offers no guarantee — and burying those three in four thousand words of boilerplate
 * makes them less likely to be read, not more.
 */
import { PwCallout, PwPageHeader } from '../components/ui'

const updated = '29 August 2026'
</script>

<template>
  <div class="legal">
    <PwPageHeader
      title="Terms of use"
      description="What you may do with this service, and what it does and does not promise you."
    >
      <template #meta>
        <p class="t-12 subtle" style="margin-top: var(--s-3)">Last updated {{ updated }}</p>
      </template>
    </PwPageHeader>

    <PwCallout tone="info" title="The short version">
      Use it for anything lawful, within the published rate limits. Do not upload documents you
      have no right to. It is free and provided as-is, so do not build something critical on the
      hosted instance without running your own copy.
    </PwCallout>

    <h2>Using the service</h2>
    <p>
      You may use the website, the API and the embeddable widget for any lawful purpose, personal
      or commercial, without an account. A free API key raises your rate limits and takes one
      request.
    </p>

    <h2>What you must not do</h2>
    <ul>
      <li>
        Upload documents you do not own or otherwise have the right to process — including anything
        confidential to someone else.
      </li>
      <li>
        Work around the rate limits: rotating keys, spreading a job across addresses, or running
        the widget as a proxy for volume you could not get directly.
      </li>
      <li>
        Use it to produce material that is unlawful, or to forge, alter or misrepresent documents
        in a way intended to deceive.
      </li>
      <li>
        Probe, scan or attempt to disrupt the service, or use it to attack anything else.
      </li>
    </ul>
    <p>
      Addresses that do these things are blocked, sometimes permanently and without notice. That is
      the whole reason the request log exists.
    </p>

    <h2>Rate limits</h2>
    <p>
      Limits are published on the <RouterLink to="/api">API page</RouterLink> and every response
      carries the quota remaining, so you can pace requests rather than discovering the ceiling by
      being refused. They are enforced per action, and they change: this is a free service, and the
      limits are whatever keeps it standing up. Nothing here entitles you to a particular quota.
    </p>

    <h2>Your documents and your content</h2>
    <p>
      You keep every right you had in anything you upload. We claim no licence over it, and it is
      discarded once the operation finishes — see the
      <RouterLink to="/privacy">privacy notice</RouterLink> for the detail, particularly the part
      about the summarise tool sending text to an AI provider.
    </p>

    <h2>No warranty</h2>
    <p>
      The service is provided as-is and as-available, without warranty of any kind. It may be
      slow, wrong, or gone tomorrow. Output is not checked for accuracy, and a converted or edited
      document may differ from what you expected — check anything that matters before you rely on
      it.
    </p>
    <p>
      To the fullest extent the law allows, the operator is not liable for lost data, lost profits,
      or any indirect or consequential loss arising from your use of the service. Nothing here
      limits liability for death, personal injury, or fraud, none of which can be excluded.
    </p>

    <h2>Availability</h2>
    <p>
      There is no uptime commitment on the hosted instance, and no support obligation. If you need
      either, the software is source-available and you can run your own copy — which is the
      intended answer for anything production-critical.
    </p>

    <h2>The software itself</h2>
    <p>
      PdfWerk is licensed under the Business Source License 1.1, which converts to Apache 2.0 on
      29 August 2030. In short: you may read, modify and self-host it freely, including
      commercially, but you may not offer it to third parties as a competing hosted service before
      that date. The
      <a href="https://github.com/joshihrn/PdfWerk/blob/main/LICENSING.md" target="_blank" rel="noopener">licence</a>
      is the authority, not this paragraph.
    </p>

    <h2>Changes and ending use</h2>
    <p>
      These terms may change; changes are made in the public repository, so its history is the
      record. Continuing to use the service after a change means accepting it. Access may be
      withdrawn from anyone breaking the rules above, and you may stop using it at any time — there
      is no account to close.
    </p>

    <p class="t-12 subtle note">
      Written to be accurate and readable rather than exhaustive, and it is not legal advice. If
      you are running this service commercially, have somebody qualified review it.
    </p>
  </div>
</template>

<style scoped>
.legal { max-width: 68ch; }

.legal h2 {
  font-size: var(--t-16);
  font-weight: var(--w-semi);
  letter-spacing: var(--track-snug);
  margin: var(--s-8) 0 var(--s-3);
  padding-top: var(--s-5);
  border-top: 1px solid var(--border);
}

.legal p,
.legal li {
  font-size: var(--t-14);
  line-height: var(--lh-base);
  color: var(--fg-muted);
  margin: 0 0 var(--s-3);
}

.legal ul { margin: 0 0 var(--s-3); padding-left: var(--s-5); }
.legal li { margin-bottom: var(--s-2); }
.legal strong { color: var(--fg); font-weight: var(--w-semi); }

.note { margin-top: var(--s-8); font-style: italic; }
</style>
