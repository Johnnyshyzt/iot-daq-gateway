import { useEffect, useState } from 'react'
import { useNavigate } from '@tanstack/react-router'
import { toast } from 'sonner'
import { ConfirmDialog } from '@/components/confirm-dialog'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Switch } from '@/components/ui/switch'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import {
  brandOfAdapter,
  canWrite,
  describeError,
  standardTemplateId,
  sdkStatusLabel,
  studioApi,
  type CatalogAdapter,
  type CatalogOverview,
  type CatalogParameter,
  type DeviceDocument,
  type DeviceTestResult,
  type PointTemplateDocument,
  type RuntimeStatus,
} from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'
import { PageShell } from './page-shell'

const noneModel = '__none__'

const emptyDevice = (): DeviceDocument => ({
  metadata: { id: '', displayName: '' },
  spec: {
    adapter: 'fanuc.sim',
    brandId: 'fanuc',
    controllerModelId: '',
    enabled: true,
    intervalMs: 1000,
    pointTemplateId: 'fanuc-catalog',
    workshop: '',
    line: '',
    connection: {
      host: '127.0.0.1',
      port: 8193,
      timeoutMs: 3000,
      focasTimeoutMs: 3000,
      path: '',
      namespace: '',
      username: '',
      password: '',
      parameters: {},
    },
  },
})

export function DevicesPage() {
  const navigate = useNavigate()
  const role = useAuthStore((state) => state.auth.user?.role[0])
  const writable = canWrite(role)
  const [devices, setDevices] = useState<DeviceDocument[]>([])
  const [templates, setTemplates] = useState<PointTemplateDocument[]>([])
  const [catalog, setCatalog] = useState<CatalogOverview | null>(null)
  const [draft, setDraft] = useState<DeviceDocument>(emptyDevice())
  const [mode, setMode] = useState<'create' | 'edit'>('create')
  const [message, setMessage] = useState('')
  const [testing, setTesting] = useState(false)
  const [testResult, setTestResult] = useState<DeviceTestResult | null>(null)
  const [runtime, setRuntime] = useState<RuntimeStatus | null>(null)
  const [confirmDelete, setConfirmDelete] = useState(false)

  async function reload() {
    const [deviceItems, templateItems, overview, status] = await Promise.all([
      studioApi<DeviceDocument[]>('/api/v1/config/devices'),
      studioApi<PointTemplateDocument[]>('/api/v1/config/point-templates'),
      studioApi<CatalogOverview>('/api/v1/catalog/brands'),
      studioApi<RuntimeStatus>('/api/v1/runtime/status').catch(() => null),
    ])
    setDevices(deviceItems)
    setTemplates(templateItems)
    setCatalog(overview)
    setRuntime(status)
    return { templateItems, overview }
  }

  useEffect(() => {
    void reload().catch((error: unknown) => setMessage(describeError(error)))
  }, [])

  function fail(error: unknown) {
    const text = describeError(error)
    setMessage(text)
    toast.error(text)
  }

  const brandId = draft.spec.brandId || brandOfAdapter(catalog, draft.spec.adapter)?.id || 'fanuc'
  const brand = catalog?.brands.find((item) => item.id === brandId)
  const adapters = [
    ...(brand?.adapters ?? []),
    ...(catalog?.genericAdapters ?? []),
  ]
  const adapter = adapters.find((item) => item.id === draft.spec.adapter) ?? brand?.adapters[0]
  const brandTemplates = templates.filter((item) => (item.spec.adapter || 'fanuc') === brandId)

  function select(device: DeviceDocument) {
    const inferred = device.spec.brandId || brandOfAdapter(catalog, device.spec.adapter)?.id || 'fanuc'
    setMode('edit')
    setDraft(structuredClone({ ...device, spec: { ...device.spec, brandId: inferred } }))
    setMessage('')
  }

  function createNew() {
    setMode('create')
    setDraft(emptyDevice())
    setMessage('')
  }

  function applyAdapter(current: DeviceDocument, next: CatalogAdapter, nextBrand: string, templateId: string) {
    const connection = { ...current.spec.connection, parameters: { ...(current.spec.connection.parameters ?? {}) } }
    for (const param of next.parameters) {
      const fallback = param.default == null ? '' : String(param.default)
      if (param.name === 'host' && param.default != null) connection.host = String(param.default)
      else if (param.name === 'port' && param.default != null) connection.port = Number(param.default)
      else if (param.name === 'timeoutMs' && param.default != null) {
        const timeout = Number(param.default)
        connection.timeoutMs = timeout
        connection.focasTimeoutMs = timeout
      } else if (param.name === 'path') connection.path = fallback
      else if (param.name === 'namespace') connection.namespace = fallback
      else if (param.name === 'username') connection.username = fallback
      else if (param.name === 'password') connection.password = ''
      else connection.parameters[param.name] = fallback
    }
    return {
      ...current,
      spec: {
        ...current.spec,
        brandId: nextBrand,
        adapter: next.id,
        pointTemplateId: templateId,
        connection,
      },
    }
  }

  function chooseBrand(nextBrandId: string) {
    const nextBrand = catalog?.brands.find((item) => item.id === nextBrandId)
    if (!nextBrand) {
      setDraft({ ...draft, spec: { ...draft.spec, brandId: nextBrandId } })
      return
    }
    const pool = [...nextBrand.adapters, ...(catalog?.genericAdapters ?? [])]
    const preferred = pool.find((item) => item.id.endsWith('.sim')) ?? pool[0]
    const templateId = templates.some((item) => item.metadata.id === standardTemplateId(nextBrand.id))
      ? standardTemplateId(nextBrand.id)
      : templates.find((item) => item.spec.adapter === nextBrand.id)?.metadata.id ?? standardTemplateId(nextBrand.id)
    const modelId = nextBrand.models[0]?.id ?? ''
    const next = preferred
      ? applyAdapter(draft, preferred, nextBrand.id, templateId)
      : { ...draft, spec: { ...draft.spec, brandId: nextBrand.id, pointTemplateId: templateId } }
    setDraft({ ...next, spec: { ...next.spec, controllerModelId: modelId } })
  }

  async function save() {
    const id = draft.metadata.id.trim()
    if (!id) {
      setMessage('请填写设备 Id')
      return
    }
    if (!draft.metadata.displayName.trim()) {
      setMessage('请填写显示名')
      return
    }
    if (!draft.spec.connection.host?.trim()) {
      setMessage('请填写设备地址')
      return
    }
    if (!Number.isFinite(draft.spec.intervalMs) || draft.spec.intervalMs < 100 || draft.spec.intervalMs > 86_400_000) {
      setMessage('采集周期需在 100 到 86400000 毫秒之间')
      return
    }
    if (!draft.spec.pointTemplateId) {
      setMessage('请选择点位模板。一类模板给多台同类设备用，不必每台重填点位。')
      return
    }
    const body: DeviceDocument = {
      ...draft,
      metadata: { ...draft.metadata, id, displayName: draft.metadata.displayName.trim() },
      spec: {
        ...draft.spec,
        brandId,
        controllerModelId: draft.spec.controllerModelId?.trim() || null,
        workshop: draft.spec.workshop?.trim() || null,
        line: draft.spec.line?.trim() || null,
        groupId: draft.spec.groupId?.trim() || null,
        connection: {
          ...draft.spec.connection,
          path: draft.spec.connection.path?.trim() || null,
          namespace: draft.spec.connection.namespace?.trim() || null,
          username: draft.spec.connection.username?.trim() || null,
          password: draft.spec.connection.password || null,
          parameters: draft.spec.connection.parameters ?? {},
        },
      },
    }
    await studioApi(`/api/v1/config/devices/${encodeURIComponent(id)}`, {
      method: 'PUT',
      body: JSON.stringify(body),
    })
    await reload()
    setMode('edit')
    setDraft(body)
    setMessage('已写入草稿。到「发布」页校验并发布后，采集才会加载这台设备。')
    toast.success('设备已保存到草稿')
  }

  async function remove(id: string) {
    await studioApi(`/api/v1/config/devices/${encodeURIComponent(id)}`, { method: 'DELETE' })
    if (draft.metadata.id === id) {
      setDraft(emptyDevice())
      setMode('create')
    }
    await reload()
    toast.success('设备已从草稿删除')
  }

  async function testSaved(id: string) {
    const result = await studioApi<DeviceTestResult>(`/api/v1/devices/${encodeURIComponent(id)}/test`, {
      method: 'POST',
    })
    setTestResult(result)
    setMessage(result.message)
    toast[result.ok ? 'success' : 'error'](result.message)
  }

  async function testDraft() {
    setTesting(true)
    try {
      const result = await studioApi<DeviceTestResult>('/api/v1/devices/test', {
        method: 'POST',
        body: JSON.stringify(draft),
      })
      setTestResult(result)
      setMessage(result.message)
      toast[result.ok ? 'success' : 'error'](result.message)
    } finally {
      setTesting(false)
    }
  }

  async function collection(id: string, enabled: boolean) {
    const result = await studioApi<{ enabled: boolean; reloaded: boolean }>(
      `/api/v1/devices/${encodeURIComponent(id)}/collection`,
      { method: 'POST', body: JSON.stringify({ enabled }) }
    )
    await reload()
    const text = result.reloaded
      ? enabled
        ? '已启动采集'
        : '已停止采集'
      : '草稿已更新。这台设备还没发布，采集不会启动。'
    setMessage(text)
    toast.success(text)
  }

  function parameterValue(param: CatalogParameter) {
    const connection = draft.spec.connection
    if (param.name === 'host') return connection.host
    if (param.name === 'port') return String(connection.port)
    if (param.name === 'timeoutMs') return String(connection.timeoutMs ?? connection.focasTimeoutMs ?? 3000)
    if (param.name === 'path') return connection.path ?? ''
    if (param.name === 'namespace') return connection.namespace ?? ''
    if (param.name === 'username') return connection.username ?? ''
    if (param.name === 'password') return connection.password ?? ''
    return connection.parameters?.[param.name] ?? ''
  }

  function setParameter(param: CatalogParameter, raw: string) {
    if (param.name === 'host') setConnection({ host: raw })
    else if (param.name === 'port') setConnection({ port: Number(raw) })
    else if (param.name === 'timeoutMs') {
      const timeout = Number(raw)
      setConnection({ timeoutMs: timeout, focasTimeoutMs: timeout })
    } else if (param.name === 'path') setConnection({ path: raw })
    else if (param.name === 'namespace') setConnection({ namespace: raw })
    else if (param.name === 'username') setConnection({ username: raw })
    else if (param.name === 'password') setConnection({ password: raw })
    else {
      setConnection({ parameters: { ...(draft.spec.connection.parameters ?? {}), [param.name]: raw } })
    }
  }

  function setConnection(patch: Partial<DeviceDocument['spec']['connection']>) {
    setDraft({
      ...draft,
      spec: { ...draft.spec, connection: { ...draft.spec.connection, ...patch } },
    })
  }

  return (
    <PageShell
      title='设备'
      description='先选品牌和控制器型号，再选适配器（模拟器或真实驱动）和该品牌的点位模板。连接参数来自适配器定义。保存写入草稿，发布后网关才会加载。模拟器不连接真实机床。'
      actions={
        <Button variant='outline' disabled={!writable} onClick={createNew}>
          新建设备
        </Button>
      }
    >
      <div className='grid gap-4 lg:grid-cols-[minmax(0,1fr)_22rem]'>
        <Card>
          <CardHeader>
            <CardTitle>设备列表</CardTitle>
          </CardHeader>
          <CardContent>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>名称</TableHead>
                  <TableHead>品牌 / 适配器</TableHead>
                  <TableHead>驱动 / 状态</TableHead>
                  <TableHead />
                </TableRow>
              </TableHeader>
              <TableBody>
                {devices.length === 0 ? (
                  <TableRow>
                    <TableCell colSpan={4} className='text-muted-foreground'>
                      还没有设备。点击「新建设备」，选择品牌后使用该品牌的模拟器。
                    </TableCell>
                  </TableRow>
                ) : null}
                {devices.map((device) => {
                  const rowBrand =
                    catalog?.brands.find((item) => item.id === device.spec.brandId) ??
                    brandOfAdapter(catalog, device.spec.adapter)
                  return (
                    <TableRow key={device.metadata.id}>
                      <TableCell>
                        <div className='font-medium'>{device.metadata.displayName}</div>
                        <div className='text-xs text-muted-foreground'>{device.metadata.id}</div>
                      </TableCell>
                      <TableCell>
                        <div>{rowBrand ? `${rowBrand.nameZh} · ${device.spec.adapter}` : device.spec.adapter}</div>
                        <div className='text-xs text-muted-foreground'>
                          {templateName(templates, device.spec.pointTemplateId)}
                        </div>
                      </TableCell>
                      <TableCell>
                        <DeviceStatus device={device} runtime={runtime} />
                      </TableCell>
                      <TableCell className='space-x-2 text-end'>
                        <Button size='sm' variant='outline' onClick={() => select(device)}>
                          编辑
                        </Button>
                        <Button
                          size='sm'
                          variant='outline'
                          onClick={() => void navigate({ to: '/live', search: { device: device.metadata.id } })}
                        >
                          实时
                        </Button>
                        <Button
                          size='sm'
                          variant='outline'
                          disabled={!writable}
                          onClick={() => void testSaved(device.metadata.id).catch(fail)}
                        >
                          测试
                        </Button>
                        <Button
                          size='sm'
                          variant='outline'
                          disabled={!writable}
                          onClick={() => void collection(device.metadata.id, !device.spec.enabled).catch(fail)}
                        >
                          {device.spec.enabled ? '停止采集' : '启动采集'}
                        </Button>
                      </TableCell>
                    </TableRow>
                  )
                })}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>{mode === 'edit' ? '编辑设备' : '新建设备'}</CardTitle>
          </CardHeader>
          <CardContent className='grid gap-3'>
            <Field label='设备 Id'>
              <Input
                value={draft.metadata.id}
                disabled={!writable || mode === 'edit'}
                onChange={(event) =>
                  setDraft({ ...draft, metadata: { ...draft.metadata, id: event.target.value } })
                }
              />
            </Field>
            {mode === 'edit' ? (
              <p className='text-xs text-muted-foreground'>设备 Id 创建后不可修改。要换 Id，请新建一台再删除原来的。</p>
            ) : null}
            <Field label='显示名'>
              <Input
                value={draft.metadata.displayName}
                disabled={!writable}
                onChange={(event) =>
                  setDraft({ ...draft, metadata: { ...draft.metadata, displayName: event.target.value } })
                }
              />
            </Field>
            <Field label='品牌'>
              <Select value={brandId} disabled={!writable || !catalog} onValueChange={chooseBrand}>
                <SelectTrigger>
                  <SelectValue placeholder='选择品牌' />
                </SelectTrigger>
                <SelectContent>
                  {(catalog?.brands ?? []).map((item) => (
                    <SelectItem key={item.id} value={item.id}>
                      {item.nameZh}（{item.nameEn}）
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>
            <Field label='控制器型号'>
              <Select
                value={draft.spec.controllerModelId || noneModel}
                disabled={!writable}
                onValueChange={(value) =>
                  setDraft({
                    ...draft,
                    spec: { ...draft.spec, controllerModelId: value === noneModel ? '' : value },
                  })
                }
              >
                <SelectTrigger>
                  <SelectValue placeholder='可选' />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={noneModel}>不指定</SelectItem>
                  {(brand?.models ?? []).map((model) => (
                    <SelectItem key={model.id} value={model.id}>
                      {model.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>
            {(brand?.models.length ?? 0) === 0 ? (
              <p className='text-xs text-muted-foreground'>该品牌在型号表里没有列出系列，控制器型号可以留空。</p>
            ) : null}
            <Field label='适配器'>
              <Select
                value={draft.spec.adapter}
                disabled={!writable || adapters.length === 0}
                onValueChange={(id) => {
                  const next = adapters.find((item) => item.id === id)
                  if (!next) return
                  setDraft(applyAdapter(draft, next, brandId, draft.spec.pointTemplateId || standardTemplateId(brandId)))
                }}
              >
                <SelectTrigger>
                  <SelectValue placeholder='选择适配器' />
                </SelectTrigger>
                <SelectContent>
                  {adapters.map((item) => (
                    <SelectItem key={item.id} value={item.id}>
                      {item.displayName}（{item.id}）
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>
            {adapter?.kind === 'stub' ? (
              <p className='text-xs text-muted-foreground'>
                {adapter.note || '该适配器还是占位实现。发布后这台设备会保持离线，请先用模拟器。'}
              </p>
            ) : adapter?.note ? (
              <p className='text-xs text-muted-foreground'>{adapter.note}</p>
            ) : null}
            <Field label='点位模板'>
              <Select
                value={draft.spec.pointTemplateId || undefined}
                disabled={!writable || brandTemplates.length === 0}
                onValueChange={(pointTemplateId) =>
                  setDraft({ ...draft, spec: { ...draft.spec, pointTemplateId } })
                }
              >
                <SelectTrigger>
                  <SelectValue placeholder={brandTemplates.length === 0 ? '该品牌还没有模板' : '选择模板'} />
                </SelectTrigger>
                <SelectContent>
                  {brandTemplates.map((template) => (
                    <SelectItem key={template.metadata.id} value={template.metadata.id}>
                      {(template.metadata.displayName || template.metadata.id) + `（${template.metadata.id}）`}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>
            <p className='text-xs text-muted-foreground'>只列出当前品牌的模板。一类模板给多台同类设备用。</p>
            {(adapter?.parameters ?? []).map((param) => (
              <Field key={param.name} label={param.label || param.name}>
                <Input
                  type={param.type === 'secret' ? 'password' : param.type === 'int' ? 'number' : 'text'}
                  value={parameterValue(param)}
                  disabled={!writable}
                  autoComplete={param.type === 'secret' ? 'new-password' : undefined}
                  onChange={(event) => setParameter(param, event.target.value)}
                />
              </Field>
            ))}
            <div className='grid grid-cols-2 gap-3'>
              <Field label='车间'>
                <Input
                  value={draft.spec.workshop ?? ''}
                  disabled={!writable}
                  onChange={(event) =>
                    setDraft({ ...draft, spec: { ...draft.spec, workshop: event.target.value } })
                  }
                />
              </Field>
              <Field label='产线'>
                <Input
                  value={draft.spec.line ?? ''}
                  disabled={!writable}
                  onChange={(event) => setDraft({ ...draft, spec: { ...draft.spec, line: event.target.value } })}
                />
              </Field>
            </div>
            <Field label='扫描周期 ms'>
              <Input
                type='number'
                value={draft.spec.intervalMs}
                disabled={!writable}
                onChange={(event) =>
                  setDraft({ ...draft, spec: { ...draft.spec, intervalMs: Number(event.target.value) } })
                }
              />
            </Field>
            <div className='flex items-center justify-between'>
              <Label>启用</Label>
              <Switch
                checked={draft.spec.enabled}
                disabled={!writable}
                onCheckedChange={(enabled) => setDraft({ ...draft, spec: { ...draft.spec, enabled } })}
              />
            </div>
            <div className='flex flex-wrap gap-2'>
              <Button disabled={!writable} onClick={() => void save().catch(fail)}>
                保存草稿
              </Button>
              <Button variant='outline' disabled={!writable || testing} onClick={() => void testDraft().catch(fail)}>
                {testing ? '正在测试…' : '测试连接'}
              </Button>
              <Button
                variant='destructive'
                disabled={!writable || mode !== 'edit' || !draft.metadata.id}
                onClick={() => setConfirmDelete(true)}
              >
                删除
              </Button>
            </div>
            {testResult ? <TestPanel result={testResult} /> : null}
            {message ? <p className='text-sm text-muted-foreground'>{message}</p> : null}
          </CardContent>
        </Card>
      </div>
      <ConfirmDialog
        open={confirmDelete}
        onOpenChange={setConfirmDelete}
        title='删除设备'
        desc={`从草稿删除「${draft.metadata.displayName || draft.metadata.id}」。已发布的采集要到发布页之后才会去掉这台设备。`}
        destructive
        confirmText='删除'
        handleConfirm={() => {
          const id = draft.metadata.id
          setConfirmDelete(false)
          void remove(id).catch(fail)
        }}
      />
    </PageShell>
  )
}

function templateName(templates: PointTemplateDocument[], id: string | null | undefined) {
  if (!id) return '未选模板'
  const template = templates.find((item) => item.metadata.id === id)
  return template?.metadata.displayName || id
}

function DeviceStatus({ device, runtime }: { device: DeviceDocument; runtime: RuntimeStatus | null }) {
  const health = runtime?.devices.find((item) => item.id === device.metadata.id)
  const text = health?.message || (device.spec.enabled ? '草稿已启用' : '草稿已禁用')
  const missing = text.includes('SDK 未安装') || text.includes('未找到 Fwlib64')
  const failed = health?.status === 'offline' || health?.status === 'degraded' || missing
  return (
    <div className='max-w-56'>
      <Badge variant={device.spec.enabled && !failed ? 'default' : 'secondary'}>
        {health ? healthLabel(health.status) : device.spec.enabled ? '启用' : '禁用'}
      </Badge>
      <div className={missing || failed ? 'mt-1 text-xs text-destructive' : 'mt-1 text-xs text-muted-foreground'}>
        {text}
      </div>
    </div>
  )
}

function healthLabel(status: string) {
  if (status === 'online') return '在线'
  if (status === 'offline') return '离线'
  if (status === 'disabled') return '已停止'
  if (status === 'degraded') return '异常'
  return status
}

function TestPanel({ result }: { result: DeviceTestResult }) {
  return (
    <div className='grid gap-2 rounded-md border p-3 text-sm'>
      <div className='font-medium'>{result.ok ? '连接测试通过' : '连接测试未通过'}</div>
      <div>网络：{result.reachable ? `可达（${result.reachableMs} ms）` : '不可达'}</div>
      <div>协议握手：{result.handshake ? `成功（${result.handshakeMs} ms）` : '未完成'}</div>
      <div>总耗时：{result.latencyMs} ms</div>
      <div>SDK：{sdkStatusLabel(result.sdkStatus)}</div>
      <p className='text-muted-foreground'>{result.message}</p>
      {result.error ? <p className='text-xs text-destructive'>{result.error}</p> : null}
      {result.samples.length > 0 ? (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>数据项</TableHead>
              <TableHead>值</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {result.samples.map((sample) => (
              <TableRow key={sample.point}>
                <TableCell className='font-mono text-xs'>{sample.point}</TableCell>
                <TableCell>
                  {sample.value ?? '—'}
                  {sample.unit ? ` ${sample.unit}` : ''}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      ) : null}
    </div>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className='grid gap-1.5'>
      <Label>{label}</Label>
      {children}
    </div>
  )
}
