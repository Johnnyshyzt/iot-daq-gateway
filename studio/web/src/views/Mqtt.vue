<script setup>
import { computed, onMounted, ref } from 'vue'
import { api } from '../api'
import { clone } from '../format'
import { canWrite, refreshWorkspace, workspace } from '../store'

const mqtt = ref(null)
const devices = ref([])
const previewDevice = ref('cnc-01')
const previewPoint = ref('state')
const error = ref('')
const message = ref('')
const pending = ref(false)
const writable = computed(() => canWrite())

onMounted(async () => {
  try {
    mqtt.value = await api('/api/v1/config/sinks/mqtt')
    devices.value = await api('/api/v1/config/devices')
    if (devices.value[0]) previewDevice.value = devices.value[0].metadata.id
  } catch (err) {
    error.value = err.message || '加载 MQTT 配置失败'
  }
})

function fill(template, point) {
  return (template || '')
    .replaceAll('{site}', workspace.siteId || 'site')
    .replaceAll('{deviceId}', previewDevice.value || 'device')
    .replaceAll('{point}', point || 'point')
}

const preview = computed(() => {
  if (!mqtt.value) return { topic: '', status: '', payload: '' }
  const topic = fill(mqtt.value.spec.topicTemplate, previewPoint.value)
  const status = fill(mqtt.value.spec.statusTopic, '')
  const payload = {
    site: workspace.siteId || 'plant-a',
    deviceId: previewDevice.value,
    point: previewPoint.value,
    value: previewPoint.value === 'state' ? 'RUNNING' : '…',
    quality: 'good',
    ts: new Date().toISOString()
  }
  return { topic, status, payload: JSON.stringify(payload, null, 2) }
})

async function save() {
  error.value = ''
  message.value = ''
  pending.value = true
  try {
    const body = clone(mqtt.value)
    body.spec.broker.usernameFromEnv = body.spec.broker.usernameFromEnv || null
    body.spec.broker.passwordFromEnv = body.spec.broker.passwordFromEnv || null
    mqtt.value = await api('/api/v1/config/sinks/mqtt', {
      method: 'PUT',
      body: JSON.stringify(body)
    })
    message.value = 'MQTT 已写入草稿。密码只保存环境变量名，不保存明文。'
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
    <section v-if="mqtt" class="split">
      <form class="panel stack" @submit.prevent="save">
        <h2>Broker</h2>
        <div class="fields">
          <div class="field">
            <label for="mqtt-id">标识</label>
            <input id="mqtt-id" v-model="mqtt.metadata.id" :disabled="!writable" />
          </div>
          <div class="field">
            <label for="mqtt-host">地址</label>
            <input id="mqtt-host" v-model="mqtt.spec.broker.host" :disabled="!writable" />
          </div>
          <div class="field">
            <label for="mqtt-port">端口</label>
            <input id="mqtt-port" v-model.number="mqtt.spec.broker.port" type="number" :disabled="!writable" />
          </div>
          <div class="field">
            <label for="mqtt-client">Client Id</label>
            <input id="mqtt-client" v-model="mqtt.spec.broker.clientId" :disabled="!writable" />
          </div>
          <div class="field">
            <label for="mqtt-user">用户名环境变量</label>
            <input id="mqtt-user" v-model="mqtt.spec.broker.usernameFromEnv" placeholder="MQTT_USER" :disabled="!writable" />
          </div>
          <div class="field">
            <label for="mqtt-password">密码环境变量</label>
            <input id="mqtt-password" v-model="mqtt.spec.broker.passwordFromEnv" placeholder="MQTT_PASSWORD" :disabled="!writable" />
          </div>
          <div class="field wide">
            <label for="mqtt-topic">主题模板</label>
            <input id="mqtt-topic" v-model="mqtt.spec.topicTemplate" :disabled="!writable" />
          </div>
          <div class="field wide">
            <label for="mqtt-status">状态主题</label>
            <input id="mqtt-status" v-model="mqtt.spec.statusTopic" :disabled="!writable" />
          </div>
          <div class="field">
            <label for="mqtt-qos">QoS</label>
            <select id="mqtt-qos" v-model.number="mqtt.spec.qos" :disabled="!writable">
              <option :value="0">0</option>
              <option :value="1">1</option>
              <option :value="2">2</option>
            </select>
          </div>
          <label class="check">
            <input v-model="mqtt.spec.retain" type="checkbox" :disabled="!writable" />
            Retain
          </label>
        </div>
        <div>
          <button class="btn primary" type="submit" :disabled="!writable || pending">保存草稿</button>
        </div>
      </form>
      <section class="panel stack">
        <h2>JSON 预览</h2>
        <div class="fields">
          <div class="field">
            <label for="preview-device">设备</label>
            <select id="preview-device" v-model="previewDevice">
              <option v-for="device in devices" :key="device.metadata.id" :value="device.metadata.id">{{ device.metadata.id }}</option>
            </select>
          </div>
          <div class="field">
            <label for="preview-point">点位</label>
            <input id="preview-point" v-model="previewPoint" />
          </div>
        </div>
        <div>
          <div class="label">点位主题</div>
          <code>{{ preview.topic }}</code>
        </div>
        <div>
          <div class="label">状态主题</div>
          <code>{{ preview.status }}</code>
        </div>
        <pre class="preview">{{ preview.payload }}</pre>
        <p class="muted">这是页面预览。网关进程实际发布时还会带上 gatewayId、quality 和 ts。</p>
      </section>
    </section>
  </main>
</template>
