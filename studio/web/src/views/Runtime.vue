<script setup>
import { onMounted, onUnmounted, ref } from 'vue'
import { api } from '../api'
import { formatTime, shortRev, statusLabel } from '../format'

const status = ref(null)
const observations = ref([])
const lines = ref([])
const deviceId = ref('')
const error = ref('')
let timer = 0

async function load() {
  try {
    const query = deviceId.value ? `?deviceId=${encodeURIComponent(deviceId.value)}&limit=50` : '?limit=50'
    const [runtime, samples, logs] = await Promise.all([
      api('/api/v1/runtime/status'),
      api(`/api/v1/runtime/observations${query}`),
      api('/api/v1/runtime/logs/tail?lines=80')
    ])
    status.value = runtime
    observations.value = samples.observations || []
    lines.value = logs.lines || []
    error.value = ''
  } catch (err) {
    error.value = err.message || '加载运行态失败'
  }
}

onMounted(() => {
  load()
  timer = window.setInterval(load, 3000)
})

onUnmounted(() => window.clearInterval(timer))
</script>

<template>
  <main class="page">
    <p v-if="status?.mode === 'live'" class="banner ok">运行态来自本机 Gateway.Host。发布配置后，网关会重新加载已发布目录。</p>
    <p v-else class="banner warn">当前运行态是模拟数据。先启动 Studio，再让 Gateway.Host 读取 studio/data/published，这里就会显示真实适配器和 MQTT 活动。</p>
    <p v-if="error" class="banner error">{{ error }}</p>
    <section class="cards">
      <article class="card">
        <div class="label">状态</div>
        <div class="value">{{ statusLabel(status?.state) }}</div>
        <div class="sub">{{ statusLabel(status?.mode) }} · {{ shortRev(status?.activeRevision) }}</div>
      </article>
      <article class="card">
        <div class="label">站点</div>
        <div class="value" style="font-size: 18px">{{ status?.name || '—' }}</div>
        <div class="sub">{{ status?.siteId }}</div>
      </article>
    </section>

    <section class="panel">
      <h2>设备</h2>
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>设备</th>
              <th>适配器</th>
              <th>状态</th>
              <th>最后见到</th>
              <th>说明</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="device in status?.devices || []" :key="device.id">
              <td>{{ device.displayName || device.id }} <span class="muted">{{ device.id }}</span></td>
              <td>{{ device.adapter }}</td>
              <td><span class="badge" :class="device.status === 'online' ? 'ok' : device.status === 'offline' ? 'bad' : 'muted'">{{ statusLabel(device.status) }}</span></td>
              <td>{{ formatTime(device.lastSeen) }}</td>
              <td>{{ device.message }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

    <section class="panel stack">
      <div class="row">
        <h2 style="margin: 0">最近观测</h2>
        <select v-model="deviceId" @change="load">
          <option value="">全部设备</option>
          <option v-for="device in status?.devices || []" :key="device.id" :value="device.id">{{ device.id }}</option>
        </select>
      </div>
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>设备</th>
              <th>点位</th>
              <th>值</th>
              <th>质量</th>
              <th>时间</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="row in observations" :key="row.deviceId + row.point">
              <td>{{ row.deviceId }}</td>
              <td>{{ row.point }}</td>
              <td>{{ row.value }}</td>
              <td>{{ row.quality }}</td>
              <td>{{ formatTime(row.timestamp) }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

    <section class="panel">
      <h2>日志尾部</h2>
      <pre class="log">{{ lines.join('\n') }}</pre>
    </section>
  </main>
</template>
