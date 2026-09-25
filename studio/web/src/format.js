export function clone(value) {
  return JSON.parse(JSON.stringify(value))
}

export function shortRev(value) {
  if (!value) return '—'
  return value.slice(0, 12)
}

export function formatTime(value) {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return String(value)
  return date.toLocaleString('zh-CN', { hour12: false })
}

export function roleLabel(role) {
  return { admin: '管理员', engineer: '工程师', viewer: '只读' }[role] || role || '—'
}

export function actionLabel(action) {
  return { publish: '发布', rollback: '回滚', seed: '初始', sync: '对齐' }[action] || action
}

export function statusLabel(status) {
  return { online: '在线', offline: '离线', disabled: '禁用', running: '运行中', mock: '模拟' }[status] || status || '—'
}
