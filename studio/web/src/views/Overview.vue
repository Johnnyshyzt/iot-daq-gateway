<script setup>
import { onMounted, ref } from 'vue'
import { api } from '../api'
import { formatTime, shortRev, statusLabel } from '../format'

const error = ref('')
const status = ref(null)
const view = ref(null)

onMounted(async () => {
  try {
    const [config, runtime] = await Promise.all([
      api('/api/v1/config'),
      api('/api/v1/runtime/status')
    ])
    view.value = config
    status.value = runtime
  } catch (err) {
    error.value = err.message || '加载失败'
  }
})

function onlineCount() {
  return (status.value?.devices || []).filter((device) => device.status === 'online').length
}
</script>

<template>
  <main class="page">
    <p v-if="error" class="banner error">{{ error }}</p>
    <section class="cards">
      <article class="card">
        <div class="label">网关</div>
        <div class="value">{{ statusLabel(status?.state) }}</div>
        <div class="sub">{{ status?.mode === 'mock' ? '运行态为模拟，未嵌入采集进程' : '已连接运行态' }}</div>
      </article>
      <article class="card">
        <div class="label">已发布修订</div>
        <div class="value mono">{{ shortRev(view?.activeRevision) }}</div>
        <div class="sub">{{ view?.dirty ? '草稿与已发布内容不同' : '草稿与已发布内容一致' }}</div>
      </article>
      <article class="card">
        <div class="label">设备在线</div>
        <div class="value">{{ status ? onlineCount() : '—' }} / {{ status?.devices?.length ?? '—' }}</div>
        <div class="sub">只统计当前已发布配置</div>
      </article>
      <article class="card">
        <div class="label">最近刷新</div>
        <div class="value" style="font-size: 16px">{{ formatTime(status?.utcNow) }}</div>
        <div class="sub">{{ view?.draft?.gateway?.metadata?.name }}</div>
      </article>
    </section>

    <section class="panel">
      <h2>最近错误</h2>
      <p v-if="!status?.recentErrors?.length" class="muted">没有匹配到的错误行。</p>
      <ul v-else>
        <li v-for="line in status.recentErrors" :key="line">{{ line }}</li>
      </ul>
    </section>

    <section class="panel">
      <h2>下一步</h2>
      <div class="row">
        <RouterLink class="btn" to="/devices">设备</RouterLink>
        <RouterLink class="btn" to="/points">点位</RouterLink>
        <RouterLink class="btn" to="/sinks/mqtt">北向 MQTT</RouterLink>
        <RouterLink class="btn primary" to="/publish">去发布</RouterLink>
      </div>
    </section>
  </main>
</template>
