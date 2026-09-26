import { useEffect, useState } from 'react'
import { useNavigate } from '@tanstack/react-router'
import { toast } from 'sonner'
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
  studioApi,
  type CatalogAdapter,
  type CatalogOverview,
  type DeviceDocument,
  type PointTemplateDocument,
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
    connection: { host: '127.0.0.1', port: 8193, timeoutMs: 3000, focasTimeoutMs: 3000, path: '', namespace: '' },
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

  async function reload() {
    const [deviceItems, templateItems, overview] = await Promise.all([
      studioApi<DeviceDocument[]>('/api/v1/config/devices'),
      studioApi<PointTemplateDocument[]>('/api/v1/config/point-templates'),
      studioApi<CatalogOverview>('/api/v1/catalog/brands'),
    ])
    setDevices(deviceItems)
    setTemplates(templateItems)
    setCatalog(overview)
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
    const connection = { ...current.spec.connection }
    for (const param of next.parameters) {
      if (param.name === 'host' && param.default != null) connection.host = String(param.default)
      if (param.name === 'port' && param.default != null) connection.port = Number(param.default)
      if (param.name === 'timeoutMs' && param.default != null) {
        const timeout = Number(param.default)
        connection.timeoutMs = timeout
        connection.focasTimeoutMs = timeout
      }
      if (param.name === 'path') connection.path = param.default == null ? '' : String(param.default)
      if (param.name === 'namespace') connection.namespace = param.default == null ? '' : String(param.default)
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

  async function test(id: string) {
    const result = await studioApi<{ ok: boolean; message: string }>(
      `/api/v1/devices/${encodeURIComponent(id)}/test`,
      { method: 'POST' }
    )
    setMessage(result.message)
    toast[result.ok ? 'success' : 'error'](result.message)
  }

  function setConnection(patch: Partial<DeviceDocument['spec']['connection']>) {
    setDraft({
      ...draft,
      spec: { ...draft.spec, connection: { ...draft.spec.connection, ...patch } },
    })
  }

  const timeoutValue = draft.spec.connection.timeoutMs ?? draft.spec.connection.focasTimeoutMs ?? 3000

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
                  <TableHead>状态</TableHead>
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
                        <Badge variant={device.spec.enabled ? 'default' : 'secondary'}>
                          {device.spec.enabled ? '启用' : '禁用'}
                        </Badge>
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
                          onClick={() => void test(device.metadata.id).catch(fail)}
                        >
                          测试
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
            {adapter && adapter.phase > 1 ? (
              <p className='text-xs text-muted-foreground'>
                {adapter.note || '真实协议驱动在第二阶段实现。发布后这台设备会保持离线，请先用模拟器。'}
              </p>
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
            {adapter?.parameters.some((param) => param.name === 'host') ? (
              <Field label='主机'>
                <Input
                  value={draft.spec.connection.host}
                  disabled={!writable}
                  onChange={(event) => setConnection({ host: event.target.value })}
                />
              </Field>
            ) : null}
            <div className='grid grid-cols-2 gap-3'>
              {adapter?.parameters.some((param) => param.name === 'port') ? (
                <Field label='端口'>
                  <Input
                    type='number'
                    value={draft.spec.connection.port}
                    disabled={!writable}
                    onChange={(event) => setConnection({ port: Number(event.target.value) })}
                  />
                </Field>
              ) : null}
              {adapter?.parameters.some((param) => param.name === 'timeoutMs') ? (
                <Field label='超时 ms'>
                  <Input
                    type='number'
                    value={timeoutValue}
                    disabled={!writable}
                    onChange={(event) => {
                      const timeout = Number(event.target.value)
                      setConnection({ timeoutMs: timeout, focasTimeoutMs: timeout })
                    }}
                  />
                </Field>
              ) : null}
            </div>
            {adapter?.parameters.some((param) => param.name === 'path') ? (
              <Field label='路径'>
                <Input
                  value={draft.spec.connection.path ?? ''}
                  disabled={!writable}
                  onChange={(event) => setConnection({ path: event.target.value })}
                />
              </Field>
            ) : null}
            {adapter?.parameters.some((param) => param.name === 'namespace') ? (
              <Field label='命名空间'>
                <Input
                  value={draft.spec.connection.namespace ?? ''}
                  disabled={!writable}
                  onChange={(event) => setConnection({ namespace: event.target.value })}
                />
              </Field>
            ) : null}
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
            <div className='flex gap-2'>
              <Button disabled={!writable} onClick={() => void save().catch(fail)}>
                保存草稿
              </Button>
              <Button
                variant='destructive'
                disabled={!writable || !draft.metadata.id}
                onClick={() => void remove(draft.metadata.id).catch(fail)}
              >
                删除
              </Button>
            </div>
            {message ? <p className='text-sm text-muted-foreground'>{message}</p> : null}
          </CardContent>
        </Card>
      </div>
    </PageShell>
  )
}

function templateName(templates: PointTemplateDocument[], id: string | null | undefined) {
  if (!id) return '未选模板'
  const template = templates.find((item) => item.metadata.id === id)
  return template?.metadata.displayName || id
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className='grid gap-1.5'>
      <Label>{label}</Label>
      {children}
    </div>
  )
}
