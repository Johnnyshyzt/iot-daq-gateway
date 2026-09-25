<script setup>
import { computed, onMounted, ref } from 'vue'
import { api } from '../api'
import { actionLabel, formatTime, shortRev } from '../format'
import { canWrite, refreshWorkspace } from '../store'

const view = ref(null)
const diff = ref({ dirty: false, changes: [] })
const revisions = ref([])
const issues = ref([])
const note = ref('')
const error = ref('')
const message = ref('')
const pending = ref(false)
const writable = computed(() => canWrite())

async function load() {
  const [config, changes, history] = await Promise.all([
    api('/api/v1/config'),
    api('/api/v1/config/diff'),
    api('/api/v1/config/revisions')
  ])
  view.value = config
  diff.value = changes
  revisions.value = history.revisions || []
  await refreshWorkspace()
}

onMounted(async () => {
  try {
    await load()
  } catch (err) {
    error.value = err.message || '加载发布信息失败'
  }
})

async function validate() {
  error.value = ''
  message.value = ''
  pending.value = true
  try {
    const result = await api('/api/v1/config/validate', { method: 'POST' })
    issues.value = result.issues || []
    message.value = result.valid ? '校验通过。' : '校验未通过。'
  } catch (err) {
    error.value = err.message || '校验失败'
    issues.value = err.details?.issues || []
  } finally {
    pending.value = false
  }
}

async function publish() {
  error.value = ''
  message.value = ''
  pending.value = true
  try {
    const result = await api('/api/v1/config/publish', {
      method: 'POST',
      body: JSON.stringify({ note: note.value })
    })
    issues.value = result.issues || []
    message.value = result.unchanged
      ? '草稿和已发布内容相同，修订未变。'
      : `已发布 ${shortRev(result.revision)}。`
    note.value = ''
    await load()
  } catch (err) {
    error.value = err.message || '发布失败'
    issues.value = err.details?.issues || []
  } finally {
    pending.value = false
  }
}

async function rollback(revision) {
  if (!confirm(`回滚到 ${shortRev(revision)}？当前草稿会被该版本覆盖。`)) return
  error.value = ''
  message.value = ''
  pending.value = true
  try {
    await api('/api/v1/config/rollback', {
      method: 'POST',
      body: JSON.stringify({ revision })
    })
    message.value = `已回滚到 ${shortRev(revision)}。`
    await load()
  } catch (err) {
    error.value = err.message || '回滚失败'
  } finally {
    pending.value = false
  }
}
</script>

<template>
  <main class="page">
    <p v-if="error" class="banner error">{{ error }}</p>
    <p v-if="message" class="banner ok">{{ message }}</p>
    <section class="cards">
      <article class="card">
        <div class="label">已发布</div>
        <div class="value mono">{{ shortRev(view?.activeRevision) }}</div>
      </article>
      <article class="card">
        <div class="label">草稿摘要</div>
        <div class="value mono">{{ shortRev(view?.draftHash) }}</div>
      </article>
      <article class="card">
        <div class="label">差异</div>
        <div class="value">{{ diff.changes?.length || 0 }}</div>
      </article>
    </section>

    <section class="panel">
      <h2>草稿差异</h2>
      <p v-if="!diff.changes?.length" class="muted">没有字段差异。</p>
      <ul v-else>
        <li v-for="change in diff.changes" :key="change.path + change.summary">
          <span class="badge" :class="change.kind === 'removed' ? 'bad' : change.kind === 'added' ? 'ok' : 'warn'">{{ change.kind }}</span>
          {{ change.summary }}
        </li>
      </ul>
    </section>

    <section class="panel stack">
      <h2>校验并发布</h2>
      <div class="field">
        <label for="note">发布说明</label>
        <input id="note" v-model="note" :disabled="!writable" placeholder="例如：增加铣床点位" />
      </div>
      <div class="row">
        <button class="btn" type="button" :disabled="!writable || pending" @click="validate">校验</button>
        <button class="btn primary" type="button" :disabled="!writable || pending" @click="publish">发布</button>
      </div>
      <p v-if="!issues.length" class="muted">发布前会再次校验。警告不会阻止发布，错误会阻止。</p>
      <ul v-else>
        <li v-for="issue in issues" :key="issue.path + issue.message">
          <span class="badge" :class="issue.severity === 'error' ? 'bad' : 'warn'">{{ issue.severity }}</span>
          <code>{{ issue.path }}</code> {{ issue.message }}
        </li>
      </ul>
    </section>

    <section class="panel">
      <h2>修订历史</h2>
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>修订</th>
              <th>动作</th>
              <th>时间</th>
              <th>说明</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(item, index) in revisions" :key="item.revision + item.createdAt + index">
              <td class="mono" :title="item.revision">{{ shortRev(item.revision) }}</td>
              <td>{{ actionLabel(item.action) }}</td>
              <td>{{ formatTime(item.createdAt) }}</td>
              <td>{{ item.note || '—' }}</td>
              <td>
                <button
                  class="btn"
                  type="button"
                  :disabled="!writable || pending || item.revision === view?.activeRevision"
                  @click="rollback(item.revision)"
                >
                  回滚
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
  </main>
</template>
