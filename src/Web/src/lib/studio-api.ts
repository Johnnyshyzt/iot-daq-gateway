import { useAuthStore } from '@/stores/auth-store'

export class StudioApiError extends Error {
  constructor(
    message: string,
    readonly status: number
  ) {
    super(message)
  }
}

export async function studioApi<T>(
  path: string,
  init: RequestInit = {}
): Promise<T> {
  const token = useAuthStore.getState().auth.accessToken
  const headers = new Headers(init.headers)
  if (init.body && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json')
  }
  if (token) {
    headers.set('Authorization', `Bearer ${token}`)
  }

  const response = await fetch(path, { ...init, headers })
  if (response.status === 401) {
    useAuthStore.getState().auth.reset()
  }
  if (!response.ok) {
    let message = response.statusText
    try {
      const body = (await response.json()) as { message?: string }
      if (body.message) message = body.message
    } catch {
      // ignore non-json errors
    }
    throw new StudioApiError(message, response.status)
  }
  if (response.status === 204) {
    return undefined as T
  }
  return (await response.json()) as T
}

export type DeviceDocument = {
  apiVersion?: string
  kind?: string
  metadata: { id: string; displayName: string }
  spec: {
    adapter: string
    enabled: boolean
    intervalMs: number
    connection: { host: string; port: number; focasTimeoutMs?: number | null }
  }
}

export type PointDefinition = {
  id: string
  address: string
  dataType: string
  unit: string
  scale: number
  deadband: number
  enabled: boolean
}

export type PointSetDocument = {
  apiVersion?: string
  kind?: string
  metadata: { deviceId: string }
  spec: { points: PointDefinition[] }
}

export type MqttSinkDocument = {
  apiVersion?: string
  kind?: string
  metadata: { id: string }
  spec: {
    broker: {
      host: string
      port: number
      clientId: string
      usernameFromEnv?: string | null
      passwordFromEnv?: string | null
      tls?: boolean
    }
    topicTemplate: string
    qos: number
    retain: boolean
    statusTopic: string
  }
}

export type GatewayDocument = {
  metadata: { siteId: string; name: string }
  spec: {
    logLevel: string
    features: { programWrite: boolean }
    acquisition: { defaultIntervalMs: number; changeOnly: boolean }
  }
}

export type ConfigView = {
  activeRevision: string
  draftHash: string
  dirty: boolean
  draft: { gateway: GatewayDocument }
}

export type RuntimeStatus = {
  name: string
  siteId: string
  state: string
  mode: string
  activeRevision: string
  utcNow: string
  devices: Array<{
    id: string
    displayName: string
    enabled: boolean
    adapter: string
    status: string
    lastSeen?: string | null
    message: string
  }>
  recentErrors: string[]
}

export function roleLabel(role: string) {
  if (role === 'admin') return '管理员'
  if (role === 'engineer') return '工程师'
  if (role === 'viewer') return '只读'
  return role
}

export function canWrite(role: string | undefined) {
  return role === 'admin' || role === 'engineer'
}
