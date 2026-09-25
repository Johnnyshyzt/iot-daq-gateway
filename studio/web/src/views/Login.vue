<script setup>
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { api } from '../api'
import { setSession } from '../store'

const router = useRouter()
const username = ref('admin')
const password = ref('admin')
const error = ref('')
const pending = ref(false)

async function submit() {
  error.value = ''
  pending.value = true
  try {
    const login = await api('/api/v1/auth/login', {
      method: 'POST',
      body: JSON.stringify({ username: username.value, password: password.value })
    })
    setSession(login)
    router.push('/')
  } catch (err) {
    error.value = err.message || '登录失败'
  } finally {
    pending.value = false
  }
}
</script>

<template>
  <div class="login-wrap">
    <form class="card login-card" @submit.prevent="submit">
      <div>
        <h1>Config Studio</h1>
        <p class="muted">登录后编辑 Fanuc 设备、点位和 MQTT，再校验发布。</p>
      </div>
      <p v-if="error" class="banner error">{{ error }}</p>
      <div class="field">
        <label for="username">用户名</label>
        <input id="username" v-model="username" autocomplete="username" />
      </div>
      <div class="field">
        <label for="password">密码</label>
        <input id="password" v-model="password" type="password" autocomplete="current-password" />
      </div>
      <button class="btn primary" type="submit" :disabled="pending">{{ pending ? '登录中…' : '登录' }}</button>
      <p class="accounts">本地桩账号：admin / admin，engineer / engineer，viewer / viewer。密码只用于本机脚手架，不要拿到生产环境。</p>
    </form>
  </div>
</template>
