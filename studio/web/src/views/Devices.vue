<script setup>
import { computed, onMounted, ref } from 'vue'
import { api } from '../api'
import { clone } from '../format'
import { canWrite, refreshWorkspace } from '../store'

const devices = ref([])
const form = ref(null)
const creating = ref(false)
const error = ref('')
const message = ref('')
const pending = ref(false)
const writable = computed(() => canWrite())

function blank() {
  return {
    apiVersion: 'daq.gateway/v1',
    kind: 'Device',
    metadata: { id: '', displayName: '' },
    spec: {
      adapter: 'fanuc.fake',
      enabled: true,
      intervalMs: 1000,
      connection: { host: '192.168.1.10', port: 8193, focasTimeoutMs: 3000 }
    }
  }
}

async function load(selectId) {
  devices.value = await api('/api/v1/config/devices')
  const match = devices.value.find((device) => device.metadata.id === selectId)
  if (match) select(match)
  else if (!form.value && devices.value[0]) select(devices.value[0])
}

onMounted(async () => {
  try {
    await load()
  } catch (err) {
    error.value = err.message || '加载设备失败'
  }
})

function select(device) {
  creating.value = false
  form.value = clone(device)
  message.value = ''
  error.value = ''
}

function startCreate() {
  creating.value = true
  form.value = blank()
  message.value = ''
  error.value = ''
}

function payload() {
  const body = clone(form.value)
  body.metadata.id = body.metadata.id.trim()
  body.metadata.displayName = body.metadata.displayName.trim()
  const timeout = body.spec.connection.focasTimeoutMs
  body.spec.connection.focasTimeoutMs = timeout === '' || timeout === null || Number.isNaN(Number(timeout))
    ? null
    : Number(timeout)
  return body
}

async function save() {
  error.value = ''
  message.value = ''
  const body = payload()
  if (!body.metadata.id) {
    error.value = '请填写设备 Id'
    return
  }
  pending.value = true
  try {
    await api(`/api/v1/config/devices/${encodeURIComponent(body.metadata.id)}`, {
      method: 'PUT',
      body: JSON.stringify(body)
    })
    creating.value = false
    await load(body.metadata.id)
    message.value = '已写入草稿。新设备会带上 state / alarm / program 三个默认点位。'
    await refreshWorkspace()
  } catch (err) {
    error.value = err.message || '保存失败'
  } finally {
    pending.value = false
  }
}

async function remove() {
  const id = form.value?.metadata?.id
  if (!id || !confirm(`删除设备 ${id} 及其点位草稿？`)) return
  pending.value = true
  error.value = ''
  try {
    await api(`/api/v1/config/devices/${encodeURIComponent(id)}`, { method: 'DELETE' })
    form.value = null
    await load()
    message.value = '设备已从草稿删除。'
    await refreshWorkspace()
  } catch (err) {
    error.value = err.message || '删除失败'
  } finally {
    pending.value = false
  }
}

async function testConnection() {
  const id = form.value?.metadata?.id
  if (!id || creating.value) {
    error.value = '请先保存设备，再做连接测试'
    return
  }
  pending.value = true
  error.value = ''
  try {
    const result = await api(`/api/v1/devices/${encodeURIComponent(id)}/test`, { method: 'POST' })
    message.value = `${result.ok ? '成功' : '失败'}：${result.message}`
  } catch (err) {
    error.value = err.message || '测试失败'
  } finally {
    pending.value = false
  }
}
</script>

<template>
  <main class="page">
    <p v-if="error" class="banner error">{{ error }}</p>
    <p v-if="message" class="banner ok">{{ message }}</p>
    <div class="split">
      <section class="stack">
        <button class="btn primary" type="button" :disabled="!writable" @click="startCreate">新建设备</button>
        <div class="list">
          <button
            v-for="device in devices"
            :key="device.metadata.id"
            type="button"
            class="item"
            :class="{ active: form && !creating && form.metadata.id === device.metadata.id }"
            @click="select(device)"
          >
            {{ device.metadata.displayName || device.metadata.id }}
            <small>{{ device.metadata.id }} · {{ device.spec.adapter }} · {{ device.spec.enabled ? '启用' : '禁用' }}</small>
          </button>
        </div>
      </section>
      <section v-if="form" class="panel">
        <h2>{{ creating ? '新建设备' : '编辑设备' }}</h2>
        <div class="fields">
          <div class="field">
            <label for="device-id">设备 Id</label>
            <input id="device-id" v-model="form.metadata.id" :disabled="!creating || !writable" />
          </div>
          <div class="field">
            <label for="device-name">显示名称</label>
            <input id="device-name" v-model="form.metadata.displayName" :disabled="!writable" />
          </div>
          <div class="field">
            <label for="device-adapter">适配器</label>
            <select id="device-adapter" v-model="form.spec.adapter" :disabled="!writable">
              <option value="fanuc.fake">fanuc.fake（模拟）</option>
              <option value="fanuc.focas">fanuc.focas</option>
            </select>
          </div>
          <div class="field">
            <label for="device-interval">采集周期（毫秒）</label>
            <input id="device-interval" v-model.number="form.spec.intervalMs" type="number" min="100" :disabled="!writable" />
          </div>
          <div class="field">
            <label for="device-host">地址</label>
            <input id="device-host" v-model="form.spec.connection.host" :disabled="!writable" />
          </div>
          <div class="field">
            <label for="device-port">端口</label>
            <input id="device-port" v-model.number="form.spec.connection.port" type="number" min="1" max="65535" :disabled="!writable" />
          </div>
          <div class="field">
            <label for="device-timeout">FOCAS 超时（毫秒）</label>
            <input id="device-timeout" v-model="form.spec.connection.focasTimeoutMs" type="number" :disabled="!writable" />
          </div>
          <label class="check">
            <input v-model="form.spec.enabled" type="checkbox" :disabled="!writable" />
            启用采集
          </label>
        </div>
        <div class="row" style="margin-top: 14px">
          <button class="btn primary" type="button" :disabled="!writable || pending" @click="save">保存草稿</button>
          <button class="btn" type="button" :disabled="!writable || pending || creating" @click="testConnection">连接测试</button>
          <button class="btn danger" type="button" :disabled="!writable || pending || creating" @click="remove">删除</button>
        </div>
        <p class="muted">M1 只接受 Fanuc。Fake 测试恒成功；FOCAS 在独立 Studio 里只探测 TCP 端口。</p>
      </section>
    </div>
  </main>
</template>
