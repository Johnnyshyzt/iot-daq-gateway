import { reactive } from 'vue'
import { api } from './api'

export const session = reactive({
  token: sessionStorage.getItem('studio_token') || '',
  username: sessionStorage.getItem('studio_user') || '',
  role: sessionStorage.getItem('studio_role') || ''
})

export const workspace = reactive({
  activeRevision: '',
  draftHash: '',
  dirty: false,
  siteName: '',
  siteId: '',
  loaded: false
})

export function setSession(login) {
  session.token = login.token
  session.username = login.username
  session.role = login.role
  sessionStorage.setItem('studio_token', login.token)
  sessionStorage.setItem('studio_user', login.username)
  sessionStorage.setItem('studio_role', login.role)
}

export function clearSession() {
  session.token = ''
  session.username = ''
  session.role = ''
  sessionStorage.removeItem('studio_token')
  sessionStorage.removeItem('studio_user')
  sessionStorage.removeItem('studio_role')
}

export function canWrite() {
  return session.role === 'admin' || session.role === 'engineer'
}

export async function refreshWorkspace() {
  const view = await api('/api/v1/config')
  workspace.activeRevision = view.activeRevision || ''
  workspace.draftHash = view.draftHash || ''
  workspace.dirty = Boolean(view.dirty)
  workspace.siteName = view.draft?.gateway?.metadata?.name || ''
  workspace.siteId = view.draft?.gateway?.metadata?.siteId || ''
  workspace.loaded = true
  return view
}
