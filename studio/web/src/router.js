import { createRouter, createWebHistory } from 'vue-router'
import { session } from './store'
import Login from './views/Login.vue'
import Overview from './views/Overview.vue'
import Devices from './views/Devices.vue'
import Points from './views/Points.vue'
import Mqtt from './views/Mqtt.vue'
import Publish from './views/Publish.vue'
import Runtime from './views/Runtime.vue'
import Settings from './views/Settings.vue'

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/login', component: Login, meta: { title: '登录', public: true } },
    { path: '/', component: Overview, meta: { title: '概览' } },
    { path: '/devices', component: Devices, meta: { title: '设备' } },
    { path: '/points', component: Points, meta: { title: '点位' } },
    { path: '/sinks/mqtt', component: Mqtt, meta: { title: '北向 MQTT' } },
    { path: '/publish', component: Publish, meta: { title: '发布' } },
    { path: '/runtime', component: Runtime, meta: { title: '运行态' } },
    { path: '/settings', component: Settings, meta: { title: '系统' } }
  ]
})

router.beforeEach((to) => {
  if (!to.meta.public && !session.token) return '/login'
  if (to.path === '/login' && session.token) return '/'
  return true
})
