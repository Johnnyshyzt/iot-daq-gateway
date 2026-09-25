<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { api } from '../api'
import { roleLabel } from '../format'
import { canWrite, refreshWorkspace } from '../store'

const form = reactive({
  siteId: '',
  name: '',
  logLevel: 'Information',
  programWrite: false,
  defaultIntervalMs: 1000,
  changeOnly: true
})
const settings = ref(null)
const error = ref('')
const message = ref('')
const pending = ref(false)
const writable = computed(() => canWrite())
const levels = ['Trace', 'Debug', 'Information', 'Warning', 'Error', 'Critical']

async function load() {
  const data = await api('/api/v1/settings')
  settings.value = data
  form.siteId = data.siteId
  form.name = data.name
  form.logLevel = data.logLevel
  form.programWrite = data.programWrite
  form.defaultIntervalMs = data.defaultIntervalMs
  form.changeOnly = data.changeOnly
}

onMounted(async () => {
  try {
    await load()
  } catch (err) {
    error.value = err.message || '加载系统设置失败'
  }
})

async function save() {
  error.value = ''
  message.value = ''
  pending.value = true
  try {
    await api('/api/v1/settings', {
      method: 'PUT',
      body: JSON.stringify(form)
    })
    message.value = '站点设置已写入草稿，发布后才会生效。'
    await load()
    await refreshWorkspace()
  } catch (err) {
    error.value = err.message || '保存失败'
  } finally {
    pending.value = false
  }
}
</script>

<template>
  <main class="page">
    <p v-if="error" class="banner error">{{ error }}</p>
    <p v-if="message" class="banner ok">{{ message }}</p>
    <section class="panel stack">
      <h2>站点</h2>
      <div class="fields">
        <div class="field">
          <label for="site-id">站点标识</label>
          <input id="site-id" v-model="form.siteId" :disabled="!writable" />
        </div>
        <div class="field">
          <label for="site-name">站点名称</label>
          <input id="site-name" v-model="form.name" :disabled="!writable" />
        </div>
        <div class="field">
          <label for="log-level">日志级别</label>
          <select id="log-level" v-model="form.logLevel" :disabled="!writable">
            <option v-for="level in levels" :key="level" :value="level">{{ level }}</option>
          </select>
        </div>
        <div class="field">
          <label for="interval">默认采集周期（毫秒）</label>
          <input id="interval" v-model.number="form.defaultIntervalMs" type="number" min="100" :disabled="!writable" />
        </div>
        <label class="check">
          <input v-model="form.changeOnly" type="checkbox" :disabled="!writable" />
          仅在点位变化时上传
        </label>
        <label class="check">
          <input v-model="form.programWrite" type="checkbox" :disabled="!writable" />
          允许程序写入（未实现，建议关闭）
        </label>
      </div>
      <div>
        <button class="btn primary" type="button" :disabled="!writable || pending" @click="save">保存草稿</button>
      </div>
      <p v-if="settings" class="muted">配置目录：<code>{{ settings.dataDirectory }}</code></p>
    </section>

    <section v-if="settings" class="panel">
      <h2>许可证</h2>
      <p><span class="badge">{{ settings.license.status }}</span> {{ settings.license.edition }}</p>
      <p>{{ settings.license.message }}</p>
    </section>

    <section class="panel">
      <h2>用户</h2>
      <p class="muted">当前用户 {{ settings?.currentUser?.username }}（{{ roleLabel(settings?.currentUser?.role) }}）。M1 是本地单机账号，不提供在线改密。</p>
      <table v-if="settings?.users?.length">
        <thead>
          <tr>
            <th>用户名</th>
            <th>角色</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="user in settings.users" :key="user.username">
            <td>{{ user.username }}</td>
            <td>{{ roleLabel(user.role) }}</td>
          </tr>
        </tbody>
      </table>
    </section>
  </main>
</template>
