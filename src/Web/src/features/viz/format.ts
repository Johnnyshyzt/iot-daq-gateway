import { useAuthStore } from '@/stores/auth-store'

export function formatClock(ms: number) {
  return new Date(ms).toLocaleTimeString('zh-CN', { hour12: false, hour: '2-digit', minute: '2-digit' })
}

export function formatTime(ms: number) {
  return new Date(ms).toLocaleString('zh-CN', {
    hour12: false,
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  })
}

export function formatPercent(value: number) {
  return `${(value * 100).toFixed(1)}%`
}

export function formatNumber(value: number | null | undefined, digits = 0) {
  if (value === null || value === undefined || Number.isNaN(value)) return '—'
  return value.toLocaleString('zh-CN', { maximumFractionDigits: digits, minimumFractionDigits: 0 })
}

export function formatMinutes(ms: number) {
  return (ms / 60000).toFixed(1)
}

export function dayStartMs(date = new Date()) {
  const copy = new Date(date)
  copy.setHours(0, 0, 0, 0)
  return copy.getTime()
}

export async function downloadFile(path: string, filename: string) {
  const token = useAuthStore.getState().auth.accessToken
  const headers = new Headers()
  if (token) headers.set('Authorization', `Bearer ${token}`)
  const response = await fetch(path, { headers })
  if (!response.ok) throw new Error('导出失败')
  const blob = await response.blob()
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = filename
  link.click()
  URL.revokeObjectURL(url)
}

export async function toggleFullscreen(element?: HTMLElement | null) {
  if (document.fullscreenElement) {
    await document.exitFullscreen()
    return
  }
  await (element ?? document.documentElement).requestFullscreen()
}
