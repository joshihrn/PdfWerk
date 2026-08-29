<script setup lang="ts">
/**
 * The privacy notice.
 *
 * Written against what the service actually does rather than from a template, because three of
 * its behaviours are the sort a reasonable person would want told plainly: uploaded documents are
 * processed in memory and never written to disk, the request log keeps raw IP addresses, and
 * summarising sends the document's text to a third-party model. A generic notice would cover none
 * of them, and the third in particular changes whether someone should use that feature at all.
 */
import { PwCallout, PwPageHeader } from '../components/ui'

const updated = '29 August 2026'
</script>

<template>
  <div class="legal">
    <PwPageHeader
      title="Privacy"
      description="What this service collects, why, how long it keeps it, and who else sees it."
    >
      <template #meta>
        <p class="t-12 subtle" style="margin-top: var(--s-3)">Last updated {{ updated }}</p>
      </template>
    </PwPageHeader>

    <PwCallout tone="info" title="The short version">
      Your documents are processed in memory and never written to disk. Requests are logged with
      your IP address to stop abuse. If you use the summarise tool, that document's text is sent to
      an AI provider — nothing else is.
    </PwCallout>

    <h2>Documents you upload</h2>
    <p>
      Files sent to this service are held in memory for as long as the operation takes and then
      discarded. They are not written to disk, not placed in a queue, not backed up, and not used
      to train anything. Nothing you upload can be retrieved afterwards, by you or by us — if you
      need the result, download it before you leave the page.
    </p>
    <p>
      The one exception is the summarise tool, described below, which necessarily sends text
      elsewhere.
    </p>

    <h2>Requests we log</h2>
    <p>
      Every API call and page view is recorded with the IP address it came from, the time, the
      method and path, the response status, how long it took, and the browser's user agent string.
      Query strings are deliberately not stored, because they carry API keys and one-time links
      often enough that keeping them would turn an audit trail into a store of credentials.
    </p>
    <p>
      This exists to enforce rate limits and to identify and block abuse. Without it a public,
      free, unauthenticated service that converts files is trivially turned into someone else's
      compute. An IP address is personal data, and we treat it as such: the log is readable only by
      an administrator, and it is the only place raw addresses are held — rate limiting itself
      works from a salted hash that cannot be reversed to an address.
    </p>
    <p>
      <strong>Retention.</strong> The log is kept for as long as the operator of this instance
      configures. On a self-hosted deployment that is whatever they choose, including indefinitely.
    </p>

    <h2>API keys</h2>
    <p>
      A key is issued on request without an account, an email address or any other identifying
      detail. We store a SHA-256 hash of it, never the key itself, along with the label you gave
      it, when it was created and how many calls it has made. Losing a key means minting another;
      it cannot be recovered, because we do not hold it.
    </p>

    <h2>The summarise tool, and AI providers</h2>
    <PwCallout tone="warn" title="This one sends your text to a third party">
      Do not summarise a document whose contents you are not willing to share with the AI provider
      configured on this instance.
    </PwCallout>
    <p>
      Summarising extracts the text of your document and sends it to a language model to be
      condensed. Depending on how this instance is configured, that provider is Google (Gemini),
      Groq, or a model running locally via Ollama. Where the provider is a third party, your
      document's text leaves this service and is subject to that provider's terms and privacy
      policy, not ours. Where it is a local Ollama model, nothing leaves the server.
    </p>
    <p>
      No other tool sends your document anywhere. Creating, converting, editing, merging,
      splitting, rotating, watermarking, protecting, inspecting and filling forms are all done on
      this server.
    </p>

    <h2>Analytics</h2>
    <p>
      We use Google Analytics to count visits and see which pages people use. It does not load at
      all until you accept it — decline, and no analytics cookies are set and no data is sent to
      Google. You can change your mind at any time from the link in the footer.
    </p>
    <p>
      When you do accept, Google Analytics sets cookies to recognise a returning browser and
      receives your IP address, the pages you view, and general details of your device and
      referrer. It never receives your documents, your API key, or anything you type into a tool.
    </p>

    <h2>What we store in your browser</h2>
    <ul>
      <li><strong>Your API key</strong>, so you do not have to paste it on every visit.</li>
      <li><strong>Your theme choice</strong>, so the site does not flash the wrong one.</li>
      <li><strong>Your analytics decision</strong>, so we stop asking.</li>
    </ul>
    <p>
      These stay in your browser and are never sent to us as cookies. Clearing your site data
      removes all three. An administrator's key is held only for the tab it was entered in, and is
      gone when that tab closes.
    </p>

    <h2>Your rights</h2>
    <p>
      If you are in the UK or the EU you have the right to ask what personal data is held about
      you, to have it corrected or erased, and to object to its processing. In practice the only
      personal data here is your IP address in the request log and, if you accepted it, what Google
      Analytics holds. To ask about either, or to have log entries for your address removed,
      contact the operator of this instance through the repository below.
    </p>

    <h2>Self-hosted instances</h2>
    <p>
      This software is source-available and anyone can run their own copy. If you are not using it
      at the operator's own domain, everything above describes the software's behaviour, but
      retention, the AI provider in use, and who can read the log are decided by whoever runs that
      instance.
    </p>

    <h2>Changes</h2>
    <p>
      Changes to this notice are made in the public repository, so its history is the record of
      what changed and when.
    </p>

    <p class="t-12 subtle note">
      This notice describes the software honestly and specifically, but it is not legal advice.
      If you are running this service commercially, have somebody qualified review it.
    </p>
  </div>
</template>

<style scoped>
/* Legal text is read, not scanned, so it gets a measure and a rhythm the tool pages do not. */
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
.legal strong { color: var(--fg); font-weight: var(--w-semi); }

.note { margin-top: var(--s-8); font-style: italic; }
</style>
