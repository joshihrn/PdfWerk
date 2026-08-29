<script setup lang="ts">
/**
 * The admin portal.
 *
 * One page with three panels behind a key prompt, rather than a section of the site with its own
 * navigation. Everything an administrator does here is either investigation or a small, reversible
 * change, and putting the log, the block list and the limits on one screen means the sequence that
 * matters — see the traffic, block the address, check it stopped — needs no navigation at all.
 */
import { computed, onMounted, onUnmounted, ref } from 'vue'
import {
  PwBadge,
  PwButton,
  PwCallout,
  PwCard,
  PwCheckbox,
  PwField,
  PwInput,
  PwPageHeader,
  PwSegmented,
} from '../components/ui'
import {
  admin,
  AdminError,
  getAdminKey,
  setAdminKey,
  type IpBlock,
  type LimitSetting,
  type RequestRecord,
} from '../api/admin'

const signedIn = ref(false)
const label = ref('')
const keyInput = ref('')
const signInError = ref<string | null>(null)
const busy = ref(false)
const error = ref<string | null>(null)
const notice = ref<string | null>(null)

const panel = ref('requests')
const panels = [
  { value: 'requests', label: 'Requests' },
  { value: 'blocks', label: 'Blocked addresses' },
  { value: 'limits', label: 'Rate limits' },
]

const requests = ref<RequestRecord[]>([])
const totalRequests = ref(0)
const addressFilter = ref('')
const autoRefresh = ref(false)

const blocks = ref<IpBlock[]>([])
const newCidr = ref('')
const newReason = ref('')

const limits = ref<LimitSetting[]>([])
const editing = ref<LimitSetting | null>(null)

let timer: number | undefined

/** Runs an admin call, turning a rejected key back into the sign-in prompt. */
async function guard(work: () => Promise<void>, success?: string) {
  busy.value = true
  error.value = null
  notice.value = null

  try {
    await work()
    if (success) notice.value = success
  } catch (ex) {
    if (ex instanceof AdminError && ex.isUnauthorised) {
      // The key has been revoked, or was never an admin one. Sitting on a dead session and
      // showing errors on every action would be worse than asking for it again.
      signedIn.value = false
      setAdminKey(null)
      signInError.value = 'That key is no longer accepted. Sign in again.'
    } else {
      error.value = ex instanceof Error ? ex.message : String(ex)
    }
  } finally {
    busy.value = false
  }
}

async function signIn() {
  const value = keyInput.value.trim()
  if (!value) return

  setAdminKey(value)
  signInError.value = null
  busy.value = true

  try {
    label.value = (await admin.whoAmI()).label
    signedIn.value = true
    keyInput.value = ''
    await loadAll()
  } catch {
    setAdminKey(null)
    signInError.value = 'That key was not accepted.'
  } finally {
    busy.value = false
  }
}

function signOut() {
  setAdminKey(null)
  signedIn.value = false
  requests.value = []
  blocks.value = []
  limits.value = []
  stopAutoRefresh()
}

async function loadAll() {
  await guard(async () => {
    const [log, blocked, limitList] = await Promise.all([
      admin.requests(100, addressFilter.value.trim() || undefined),
      admin.blocks(),
      admin.limits(),
    ])

    requests.value = log.requests
    totalRequests.value = log.total
    blocks.value = blocked
    limits.value = limitList
  })
}

async function loadRequests() {
  await guard(async () => {
    const log = await admin.requests(100, addressFilter.value.trim() || undefined)
    requests.value = log.requests
    totalRequests.value = log.total
  })
}

function toggleAutoRefresh(on: boolean) {
  autoRefresh.value = on
  if (on) timer = window.setInterval(loadRequests, 5000)
  else stopAutoRefresh()
}

function stopAutoRefresh() {
  if (timer) window.clearInterval(timer)
  timer = undefined
  autoRefresh.value = false
}

async function addBlock() {
  const cidr = newCidr.value.trim()
  if (!cidr) return

  await guard(async () => {
    await admin.block(cidr, newReason.value.trim() || 'no reason given', null)
    blocks.value = await admin.blocks()
    newCidr.value = ''
    newReason.value = ''
  }, `Blocked ${cidr}.`)
}

/** Fills the block form from a log row, so the common case is two clicks rather than retyping. */
function blockFrom(record: RequestRecord) {
  newCidr.value = record.address
  newReason.value = `seen at ${record.path}`
  panel.value = 'blocks'
}

async function removeBlock(block: IpBlock) {
  await guard(async () => {
    await admin.unblock(block.id)
    blocks.value = await admin.blocks()
  }, `Unblocked ${block.cidr}.`)
}

async function saveLimit() {
  const setting = editing.value
  if (!setting) return

  await guard(async () => {
    await admin.saveLimit(setting)
    limits.value = await admin.limits()
    editing.value = null
  }, 'Limit saved. It applies to the next request.')
}

async function resetLimit(setting: LimitSetting) {
  await guard(async () => {
    await admin.resetLimit(setting.tier, setting.action)
    limits.value = await admin.limits()
    if (editing.value?.tier === setting.tier && editing.value?.action === setting.action) editing.value = null
  }, 'Reset. That limit follows configuration again.')
}

const blockedAddresses = computed(() => new Set(blocks.value.filter((b) => b.active).map((b) => b.cidr)))

function statusTone(status: number) {
  if (status >= 500) return 'bad'
  if (status === 429) return 'warn'
  if (status >= 400) return 'warn'
  return 'ok'
}

function when(value: string) {
  return new Date(value).toLocaleString()
}

onMounted(async () => {
  if (!getAdminKey()) return

  try {
    label.value = (await admin.whoAmI()).label
    signedIn.value = true
    await loadAll()
  } catch {
    setAdminKey(null)
  }
})

onUnmounted(stopAutoRefresh)
</script>

<template>
  <div>
    <PwPageHeader
      title="Administration"
      description="Who is calling, what they asked for, and what they are allowed to do."
    >
      <template v-if="signedIn" #actions>
        <PwBadge tone="accent">{{ label }}</PwBadge>
        <PwButton size="sm" @click="signOut">Sign out</PwButton>
      </template>
    </PwPageHeader>

    <!-- ---- sign in ---- -->
    <PwCard v-if="!signedIn" title="Administrator key">
      <PwCallout v-if="signInError" tone="bad" assertive class="mb">{{ signInError }}</PwCallout>

      <div class="stack-4">
        <PwField
          v-slot="{ id }"
          label="Key"
          help="Held for this tab only, and cleared when you close it."
        >
          <PwInput
            :id="id"
            v-model="keyInput"
            type="password"
            mono
            placeholder="pw_…"
            @keyup.enter="signIn"
          />
        </PwField>

        <div>
          <PwButton variant="solid" :loading="busy" :disabled="!keyInput.trim()" @click="signIn">
            Sign in
          </PwButton>
        </div>
      </div>
    </PwCard>

    <template v-else>
      <PwCallout v-if="error" tone="bad" assertive class="mb">{{ error }}</PwCallout>
      <PwCallout v-if="notice" tone="ok" class="mb">{{ notice }}</PwCallout>

      <div class="tabs">
        <PwSegmented v-model="panel" :options="panels" label="Admin section" />
      </div>

      <!-- ---- requests ---- -->
      <PwCard v-if="panel === 'requests'" title="Recent requests" flush>
        <template #actions>
          <PwBadge tone="neutral">{{ totalRequests.toLocaleString() }} logged</PwBadge>
        </template>

        <div class="toolbar">
          <PwField v-slot="{ id }" label="Filter by address" hide-label class="grow">
            <PwInput
              :id="id"
              v-model="addressFilter"
              mono
              placeholder="Filter by address, e.g. 203.0.113.4"
              @keyup.enter="loadRequests"
            />
          </PwField>

          <PwButton size="sm" :loading="busy" @click="loadRequests">Refresh</PwButton>

          <PwCheckbox
            :model-value="autoRefresh"
            label="Auto"
            help="Every 5s"
            @update:model-value="toggleAutoRefresh"
          />
        </div>

        <!--
          "Nothing logged yet" while a fetch is still in flight is a lie that reads as an answer.
          The distinction matters most on the first load, which is exactly when the reader has no
          other way to tell the difference.
        -->
        <p v-if="busy && !requests.length" class="empty">Loading…</p>
        <p v-else-if="!requests.length" class="empty">Nothing logged yet.</p>

        <table v-if="requests.length" class="table">
          <thead>
            <tr>
              <th scope="col">When</th>
              <th scope="col">Address</th>
              <th scope="col">Request</th>
              <th scope="col" class="num">Status</th>
              <th scope="col" class="num">Time</th>
              <th scope="col"></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="record in requests" :key="record.id" :class="{ 'is-blocked': record.blocked }">
              <td class="t-12 nowrap">{{ when(record.at) }}</td>
              <td class="mono t-12">
                {{ record.address }}
                <PwBadge v-if="blockedAddresses.has(record.address)" tone="bad" dot>blocked</PwBadge>
              </td>
              <td class="t-12 truncate" :title="`${record.method} ${record.path}`">
                <span class="mono">{{ record.method }}</span> {{ record.path }}
              </td>
              <td class="num">
                <PwBadge :tone="statusTone(record.statusCode)">{{ record.statusCode }}</PwBadge>
              </td>
              <td class="num t-12">{{ record.elapsedMs }} ms</td>
              <td class="num">
                <PwButton size="sm" variant="ghost" @click="blockFrom(record)">Block</PwButton>
              </td>
            </tr>
          </tbody>
        </table>
      </PwCard>

      <!-- ---- blocks ---- -->
      <div v-else-if="panel === 'blocks'" class="split">
        <PwCard title="Block an address or range">
          <div class="stack-4">
            <PwField
              v-slot="{ id }"
              label="Address or range"
              help="203.0.113.4 for one address, 203.0.113.0/24 for a range. IPv6 works the same way."
            >
              <PwInput :id="id" v-model="newCidr" mono placeholder="203.0.113.0/24" />
            </PwField>

            <PwField v-slot="{ id }" label="Reason" help="Recorded against the block, for whoever reads this later">
              <PwInput :id="id" v-model="newReason" placeholder="Repeated 429s across every endpoint" />
            </PwField>

            <div>
              <PwButton variant="danger" :loading="busy" :disabled="!newCidr.trim()" @click="addBlock">
                Block it
              </PwButton>
            </div>
          </div>

          <template #footer>
            <span class="t-12 subtle">
              A block takes effect immediately and costs the service nothing to enforce.
            </span>
          </template>
        </PwCard>

        <PwCard title="Blocked" flush>
          <p v-if="busy && !blocks.length" class="empty">Loading…</p>
          <p v-else-if="!blocks.length" class="empty">Nothing is blocked.</p>

          <table v-if="blocks.length" class="table">
            <thead>
              <tr>
                <th scope="col">Range</th>
                <th scope="col">Reason</th>
                <th scope="col">Added</th>
                <th scope="col"></th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="block in blocks" :key="block.id">
                <td class="mono t-12">
                  {{ block.cidr }}
                  <PwBadge v-if="!block.active" tone="neutral">expired</PwBadge>
                </td>
                <td class="t-12 truncate" :title="block.reason">{{ block.reason }}</td>
                <td class="t-12 nowrap">{{ when(block.createdAt) }}</td>
                <td class="num">
                  <PwButton size="sm" :loading="busy" @click="removeBlock(block)">Unblock</PwButton>
                </td>
              </tr>
            </tbody>
          </table>
        </PwCard>
      </div>

      <!-- ---- limits ---- -->
      <div v-else class="split">
        <PwCard title="Rate limits" flush>
          <table class="table">
            <thead>
              <tr>
                <th scope="col">Tier</th>
                <th scope="col">Action</th>
                <th scope="col" class="num">Minute</th>
                <th scope="col" class="num">Hour</th>
                <th scope="col" class="num">Day</th>
                <th scope="col"></th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="setting in limits" :key="`${setting.tier}-${setting.action}`">
                <td class="t-12">{{ setting.tier }}</td>
                <td class="t-12">
                  {{ setting.action || 'default' }}
                  <PwBadge v-if="setting.isOverride" tone="accent">changed</PwBadge>
                </td>
                <td class="num t-12">{{ setting.perMinute }}</td>
                <td class="num t-12">{{ setting.perHour }}</td>
                <td class="num t-12">{{ setting.perDay }}</td>
                <td class="num nowrap">
                  <PwButton size="sm" variant="ghost" @click="editing = { ...setting }">Edit</PwButton>
                  <PwButton
                    v-if="setting.isOverride"
                    size="sm"
                    variant="ghost"
                    :loading="busy"
                    @click="resetLimit(setting)"
                  >
                    Reset
                  </PwButton>
                </td>
              </tr>
            </tbody>
          </table>
        </PwCard>

        <PwCard v-if="editing" :title="`${editing.tier} · ${editing.action || 'default'}`">
          <div class="grid">
            <PwField v-slot="{ id }" label="Per minute">
              <PwInput :id="id" v-model.number="editing.perMinute" type="number" min="0" />
            </PwField>
            <PwField v-slot="{ id }" label="Per hour">
              <PwInput :id="id" v-model.number="editing.perHour" type="number" min="0" />
            </PwField>
            <PwField v-slot="{ id }" label="Per day">
              <PwInput :id="id" v-model.number="editing.perDay" type="number" min="0" />
            </PwField>
            <PwField v-slot="{ id }" label="Concurrent">
              <PwInput :id="id" v-model.number="editing.concurrent" type="number" min="0" />
            </PwField>
            <PwField v-slot="{ id }" label="Max upload bytes">
              <PwInput :id="id" v-model.number="editing.maxUploadBytes" type="number" min="0" />
            </PwField>
            <PwField v-slot="{ id }" label="Max pages">
              <PwInput :id="id" v-model.number="editing.maxPages" type="number" min="0" />
            </PwField>
            <PwField v-slot="{ id }" label="Max batch">
              <PwInput :id="id" v-model.number="editing.maxBatch" type="number" min="0" />
            </PwField>
            <PwField v-slot="{ id }" label="Max characters">
              <PwInput :id="id" v-model.number="editing.maxCharacters" type="number" min="0" />
            </PwField>
          </div>

          <template #footer>
            <PwButton variant="solid" :loading="busy" @click="saveLimit">Save</PwButton>
            <PwButton @click="editing = null">Cancel</PwButton>
            <span class="t-12 subtle right">Applies to the next request, not the next restart.</span>
          </template>
        </PwCard>

        <PwCard v-else title="Editing">
          <p class="t-13 subtle">
            Pick a row to change it. Anything left alone keeps following the configuration file, so
            a deployment can still move the defaults underneath you.
          </p>
        </PwCard>
      </div>
    </template>
  </div>
</template>

<style scoped>
.mb { margin-bottom: var(--s-4); }

.tabs { margin-bottom: var(--s-4); }

.toolbar {
  display: flex;
  align-items: center;
  gap: var(--s-3);
  padding: var(--s-3) var(--s-4);
  border-bottom: 1px solid var(--border);
}

.grow { flex: 1 1 auto; }

.empty {
  padding: var(--s-8) var(--s-4);
  text-align: center;
  color: var(--fg-subtle);
  font-size: var(--t-13);
}

.mono { font-family: var(--mono); }

.nowrap { white-space: nowrap; }

/* A refused request should be findable by eye when scanning a full page of them. */
.is-blocked { background: var(--bad-bg); }

.grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
  gap: var(--s-3);
}
</style>
