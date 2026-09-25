<script setup>
import { computed, onMounted, ref } from 'vue'
import { api } from '../api'
import { canWrite, refreshWorkspace } from '../store'

const devices = ref([])
const deviceId = ref('')
const points = ref([])
const error = ref('')
const message = ref('')
const pending = ref(false)
const writable = computed(() => canWrite())

async function loadDevices() {
  devices.value = await api('/api/v1/config/devices')
  if (!deviceId.value && devices.value[0]) {
    deviceId.value = devices.value[0].metadata.id
  }
}

async function loadPoints() {
  if (!deviceId.value) {
    points.value = []
    return
  }
  const document = await api(`/api/v1/config/points/${encodeURIComponent(deviceId.value)}`)
  points.value = document.spec?.points || []
}

onMounted(async () => {
  try {
    await loadDevices()
    await loadPoints()
  } catch (err) {
    error.value = err.message || '加载点位失败'
  }
})

async function changeDevice() {
  message.value = ''
  error.value = ''
  try {
    await loadPoints()
  } catch (err) {
    error.value = err.message || '加载点位失败'
  }
}

function addRow() {
  points.value.push({
    id: '',
    address: '',
    dataType: 'string',
    unit: '',
    scale: 1,
    deadband: 0,
    enabled: true
  })
}

function removeRow(index) {
  points.value.splice(index, 1)
}

async function save() {
  error.value = ''
  message.value = ''
  pending.value = true
  try {
    await api(`/api/v1/config/points/${encodeURIComponent(deviceId.value)}`, {
      method: 'PUT',
      body: JSON.stringify({
        apiVersion: 'daq.gateway/v1',
        kind: 'PointSet',
        metadata: { deviceId: deviceId.value },
        spec: { points: points.value }
      })
    })
    message.value = '点位已写入草稿。'
    await loadPoints()
    await refreshWorkspace()
  } catch (err) {
    error.value = err.message || '保存失败'
  } finally {
    pending.value = false
  }
}

function csvCell(value) {
  const text = value === null || value === undefined ? '' : String(value)
  if (/[",\n]/.test(text)) return `"${text.replaceAll('"', '""')}"`
  return text
}

function exportCsv() {
  const header = ['id', 'address', 'dataType', 'unit', 'scale', 'deadband', 'enabled']
  const lines = [header.join(',')]
  for (const point of points.value) {
    lines.push(header.map((key) => csvCell(point[key])).join(','))
  }
  const blob = new Blob([lines.join('\n')], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = `${deviceId.value || 'points'}.csv`
  link.click()
  URL.revokeObjectURL(url)
}

function parseCsv(text) {
  const rows = []
  let row = []
  let cell = ''
  let quoted = false
  const source = text.replace(/^\uFEFF/, '')
  for (let i = 0; i < source.length; i += 1) {
    const char = source[i]
    if (quoted) {
      if (char === '"') {
        if (source[i + 1] === '"') {
          cell += '"'
          i += 1
        } else {
          quoted = false
        }
      } else {
        cell += char
      }
      continue
    }
    if (char === '"') {
      quoted = true
    } else if (char === ',') {
      row.push(cell)
      cell = ''
    } else if (char === '\n') {
      row.push(cell)
      rows.push(row)
      row = []
      cell = ''
    } else if (char !== '\r') {
      cell += char
    }
  }
  row.push(cell)
  if (row.some((value) => value !== '')) rows.push(row)
  return rows
}

async function importCsv(event) {
  const file = event.target.files?.[0]
  event.target.value = ''
  if (!file) return
  const rows = parseCsv(await file.text())
  if (rows.length < 2) {
    error.value = 'CSV 至少需要表头和一行数据'
    return
  }
  const header = rows[0].map((item) => item.trim())
  points.value = rows.slice(1).filter((columns) => columns.some((value) => value.trim() !== '')).map((columns) => {
    const record = Object.fromEntries(header.map((key, index) => [key, columns[index] ?? '']))
    return {
      id: record.id?.trim() || '',
      address: record.address?.trim() || '',
      dataType: record.dataType?.trim() || 'string',
      unit: record.unit || '',
      scale: Number(record.scale || 1),
      deadband: Number(record.deadband || 0),
      enabled: !['false', '0', 'no'].includes(String(record.enabled).toLowerCase())
    }
  })
  message.value = '已从 CSV 填入表格，点击保存草稿后才会写入。'
}
</script>

<template>
  <main class="page">
    <p v-if="error" class="banner error">{{ error }}</p>
    <p v-if="message" class="banner ok">{{ message }}</p>
    <section class="panel stack">
      <div class="row">
        <div class="field">
          <label for="point-device">设备</label>
          <select id="point-device" v-model="deviceId" @change="changeDevice">
            <option v-for="device in devices" :key="device.metadata.id" :value="device.metadata.id">
              {{ device.metadata.displayName || device.metadata.id }}（{{ device.metadata.id }}）
            </option>
          </select>
        </div>
        <button class="btn" type="button" :disabled="!deviceId" @click="addRow">添加点位</button>
        <button class="btn" type="button" :disabled="!deviceId" @click="exportCsv">导出 CSV</button>
        <label class="btn">
          导入 CSV
          <input type="file" accept=".csv,text/csv" hidden :disabled="!writable || !deviceId" @change="importCsv" />
        </label>
        <button class="btn primary" type="button" :disabled="!writable || !deviceId || pending" @click="save">保存草稿</button>
      </div>
      <p class="muted">切换设备会丢掉还没保存的表格修改。Excel 请另存为 CSV。列：id, address, dataType, unit, scale, deadband, enabled。</p>
      <p v-if="!devices.length" class="banner warn">还没有设备。先在设备页添加一台 Fanuc。</p>
      <div v-else class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Id</th>
              <th>地址</th>
              <th>类型</th>
              <th>单位</th>
              <th>倍率</th>
              <th>死区</th>
              <th>启用</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(point, index) in points" :key="index">
              <td><input v-model="point.id" :disabled="!writable" /></td>
              <td><input v-model="point.address" :disabled="!writable" /></td>
              <td>
                <select v-model="point.dataType" :disabled="!writable">
                  <option value="string">string</option>
                  <option value="number">number</option>
                  <option value="int">int</option>
                  <option value="float">float</option>
                  <option value="bool">bool</option>
                </select>
              </td>
              <td><input v-model="point.unit" :disabled="!writable" /></td>
              <td><input v-model.number="point.scale" type="number" step="any" :disabled="!writable" /></td>
              <td><input v-model.number="point.deadband" type="number" step="any" min="0" :disabled="!writable" /></td>
              <td><input v-model="point.enabled" type="checkbox" :disabled="!writable" /></td>
              <td><button class="btn danger" type="button" :disabled="!writable" @click="removeRow(index)">删除</button></td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
  </main>
</template>
