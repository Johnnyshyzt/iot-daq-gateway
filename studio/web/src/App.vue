<script setup>
import { computed, onMounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { clearSession, refreshWorkspace, session, workspace } from './store'
import { roleLabel, shortRev } from './format'

const route = useRoute()
const router = useRouter()
const isLogin = computed(() => route.path === '/login')
const title = computed(() => route.meta.title || 'Config Studio')
const nav = [
  { to: '/', label: '概览' },
  { to: '/devices', label: '设备' },
  { to: '/points', label: '点位' },
  { to: '/sinks/mqtt', label: '北向 MQTT' },
  { to: '/publish', label: '发布' },
  { to: '/runtime', label: '运行态' },
  { to: '/settings', label: '系统' }
]

async function loadWorkspace() {
  if (!session.token || route.path === '/login') return
  try {
    await refreshWorkspace()
  } catch {
    // 401 redirects to login.
  }
}

onMounted(loadWorkspace)
watch(() => route.path, loadWorkspace)

function logout() {
  clearSession()
  router.push('/login')
}
</script>

<template>
  <RouterView v-if="isLogin" />
  <div v-else class="shell">
    <aside class="sidebar">
      <div class="brand">
        <strong>Config Studio</strong>
        <span>采集网关配置</span>
      </div>
      <nav class="nav">
        <RouterLink v-for="item in nav" :key="item.to" :to="item.to">{{ item.label }}</RouterLink>
      </nav>
      <p class="hint">M1 脚手架。配置写在 YAML 草稿里，发布后才成为已生效版本。</p>
    </aside>
    <section class="main">
      <header class="topbar">
        <div>
          <h1>{{ title }}</h1>
          <div class="muted">{{ workspace.siteName || '未命名站点' }} · {{ workspace.siteId || '—' }}</div>
        </div>
        <div class="top-meta">
          <span class="badge" :class="workspace.dirty ? 'warn' : 'ok'">{{ workspace.dirty ? '草稿未发布' : '草稿已同步' }}</span>
          <span class="badge mono" :title="workspace.activeRevision">修订 {{ shortRev(workspace.activeRevision) }}</span>
          <span class="badge">{{ session.username }} · {{ roleLabel(session.role) }}</span>
          <button class="btn" type="button" @click="logout">退出</button>
        </div>
      </header>
      <RouterView />
    </section>
  </div>
</template>
