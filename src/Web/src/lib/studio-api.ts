import { useAuthStore } from '@/stores/auth-store'

export type ValidationIssue = {
  severity?: string
  path?: string
  message: string
}

export class StudioApiError extends Error {
  readonly status: number
  readonly issues: ValidationIssue[]

  constructor(message: string, status: number, issues: ValidationIssue[] = []) {
    super(message)
    this.name = 'StudioApiError'
    this.status = status
    this.issues = issues
  }
}

const statusText: Record<number, string> = {
  400: '请求无效',
  401: '需要登录',
  403: '当前角色无权执行此操作',
  404: '资源不存在',
  409: '配置冲突',
  422: '校验未通过',
  500: '服务器内部错误',
}

export function describeError(error: unknown) {
  if (error instanceof StudioApiError) return error.message
  if (error instanceof Error && error.message) return error.message
  return '操作失败'
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

  let response: Response
  try {
    response = await fetch(path, { ...init, headers })
  } catch {
    throw new StudioApiError('无法连接管理接口。请确认 Host 已在本机启动。', 0)
  }

  if (response.status === 401 && !path.endsWith('/auth/login')) {
    useAuthStore.getState().auth.reset()
    if (!window.location.pathname.startsWith('/sign-in')) {
      const redirect = window.location.pathname + window.location.search
      window.location.assign(`/sign-in?redirect=${encodeURIComponent(redirect)}`)
    }
  }
  if (!response.ok) {
    throw await readError(response)
  }
  if (response.status === 204) {
    return undefined as T
  }
  return (await response.json()) as T
}

async function readError(response: Response) {
  let message = statusText[response.status] ?? '请求失败'
  let issues: ValidationIssue[] = []
  try {
    const body = (await response.json()) as {
      message?: string
      title?: string
      details?: { issues?: unknown }
      errors?: Record<string, string[]>
    }
    if (body.message) message = body.message
    else if (body.errors) message = '请求格式不正确'
    else if (body.title && response.status >= 500) message = '服务器内部错误'
    issues = readIssues(body.details?.issues)
  } catch {
    // 非 JSON 错误沿用状态码对应的中文说明
  }

  if (issues.length > 0) {
    const detail = issues
      .map((issue) => (issue.path ? `${issue.path}：${issue.message}` : issue.message))
      .join('；')
    message = `${message}。${detail}`
  }
  return new StudioApiError(message, response.status, issues)
}

function readIssues(value: unknown): ValidationIssue[] {
  if (!Array.isArray(value)) return []
  return value.flatMap((item) => {
    if (!item || typeof item !== 'object') return []
    const record = item as { severity?: unknown; path?: unknown; message?: unknown }
    if (typeof record.message !== 'string' || !record.message) return []
    return [
      {
        severity: typeof record.severity === 'string' ? record.severity : 'error',
        path: typeof record.path === 'string' ? record.path : '',
        message: record.message,
      },
    ]
  })
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
  draft: {
    gateway: GatewayDocument
    devices?: DeviceDocument[]
    pointSets?: PointSetDocument[]
    mqtt?: MqttSinkDocument
  }
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
    statusTopic?: string
  }>
  recentErrors: string[]
}

export const pointDataTypes = [
  { value: 'string', label: 'string 文本' },
  { value: 'bool', label: 'bool 布尔' },
  { value: 'int32', label: 'int32 整数' },
  { value: 'int64', label: 'int64 长整数' },
  { value: 'float', label: 'float 单精度' },
  { value: 'double', label: 'double 双精度' },
] as const

export function normalizeDataType(value: string) {
  if (value === 'int') return 'int32'
  if (value === 'number') return 'double'
  return value || 'string'
}

export function expandTopic(
  template: string,
  values: { site?: string; deviceId?: string; point?: string }
) {
  let result = template.split('{site}').join(values.site ?? '').split('{deviceId}').join(values.deviceId ?? '')
  if (values.point !== undefined) {
    result = result.split('{point}').join(values.point)
  }
  return result
}

export function actionLabel(action: string) {
  if (action === 'publish') return '发布'
  if (action === 'rollback') return '回滚'
  if (action === 'seed') return '初始'
  if (action === 'sync') return '对齐'
  return action
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
