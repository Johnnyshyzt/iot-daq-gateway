import { useEffect, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { Textarea } from '@/components/ui/textarea'
import { PageShell } from '@/features/daq/page-shell'
import {
  canWrite,
  describeError,
  studioApi,
  type DeviceDocument,
  type PointTemplateDocument,
} from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'

type ComputedPoint = {
  id: string
  scope: string
  ownerId: string
  pointId: string
  name: string
  unit: string
  expression: string
  enabled: boolean
}

type FunctionHelp = { name: string; text: string }

type Preview = {
  ok: boolean
  value?: number | null
  text?: string | null
  error?: string | null
  references?: string[]
}

const blank = (): ComputedPoint => ({
  id: crypto.randomUUID().replace(/-/g, ''),
  scope: 'device',
  ownerId: '',
  pointId: '',
  name: '',
  unit: '',
  expression: '',
  enabled: true,
})

export function ComputedPage() {
  const writable = canWrite(useAuthStore((state) => state.auth.user?.role[0]))
  const [points, setPoints] = useState<ComputedPoint[]>([])
  const [help, setHelp] = useState<FunctionHelp[]>([])
  const [devices, setDevices] = useState<DeviceDocument[]>([])
  const [templates, setTemplates] = useState<PointTemplateDocument[]>([])
  const [draft, setDraft] = useState<ComputedPoint>(blank())
  const [previewDevice, setPreviewDevice] = useState('')
  const [preview, setPreview] = useState<Preview | null>(null)
  const [error, setError] = useState('')

  async function load() {
    const [computed, deviceList, templateList] = await Promise.all([
      studioApi<{ points: ComputedPoint[]; functions: FunctionHelp[] }>('/api/v1/computed'),
      studioApi<DeviceDocument[]>('/api/v1/config/devices'),
      studioApi<PointTemplateDocument[]>('/api/v1/config/point-templates'),
    ])
    setPoints(computed.points)
    setHelp(computed.functions)
    setDevices(deviceList)
    setTemplates(templateList)
    setPreviewDevice((current) => current || deviceList[0]?.metadata.id || '')
  }

  useEffect(() => {
    // Data load on mount. The request resolves after the effect returns.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load().catch((err: unknown) => setError(describeError(err)))
  }, [])

  const owners = draft.scope === 'template' ? templates.map((item) => ({ id: item.metadata.id, label: item.metadata.displayName || item.metadata.id })) : devices.map((item) => ({ id: item.metadata.id, label: item.metadata.displayName || item.metadata.id }))

  async function save() {
    try {
      await studioApi(`/api/v1/computed/${draft.id}`, { method: 'PUT', body: JSON.stringify(draft) })
      setError('')
      await load()
    } catch (err) {
      setError(describeError(err))
    }
  }

  async function remove(id: string) {
    try {
      await studioApi(`/api/v1/computed/${id}`, { method: 'DELETE' })
      if (draft.id === id) setDraft(blank())
      await load()
    } catch (err) {
      setError(describeError(err))
    }
  }

  async function runPreview() {
    try {
      const result = await studioApi<Preview>('/api/v1/computed/preview', {
        method: 'POST',
        body: JSON.stringify({ expression: draft.expression, deviceId: previewDevice }),
      })
      setPreview(result)
      setError(result.ok ? '' : result.error || '表达式无效')
    } catch (err) {
      setError(describeError(err))
    }
  }

  const previewText = preview?.ok
    ? preview.value !== null && preview.value !== undefined
      ? String(preview.value)
      : preview.text || '（空）'
    : preview?.error || '尚未预览'

  return (
    <PageShell
      title='计算点'
      description='用沙箱表达式从已有点位算出虚拟点。计算点进入历史、MQTT、HTTP 和 OPC UA，并标成计算点。社区版可用，不执行任意代码。'
    >
      {error ? <p className='mb-3 text-sm text-destructive'>{error}</p> : null}
      <div className='grid gap-4 lg:grid-cols-[1.2fr_0.8fr]'>
        <Card>
          <CardHeader>
            <CardTitle>表达式</CardTitle>
          </CardHeader>
          <CardContent className='grid gap-3'>
            <div className='grid gap-3 sm:grid-cols-2'>
              <label className='grid gap-1 text-xs text-muted-foreground'>
                名称
                <Input value={draft.name} disabled={!writable} onChange={(event) => setDraft({ ...draft, name: event.target.value })} />
              </label>
              <label className='grid gap-1 text-xs text-muted-foreground'>
                点位 Id
                <Input value={draft.pointId} disabled={!writable} onChange={(event) => setDraft({ ...draft, pointId: event.target.value })} placeholder='calc_loadHigh' />
              </label>
              <label className='grid gap-1 text-xs text-muted-foreground'>
                范围
                <Select value={draft.scope} onValueChange={(scope) => setDraft({ ...draft, scope, ownerId: '' })} disabled={!writable}>
                  <SelectTrigger><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value='device'>单台设备</SelectItem>
                    <SelectItem value='template'>点位模板</SelectItem>
                  </SelectContent>
                </Select>
              </label>
              <label className='grid gap-1 text-xs text-muted-foreground'>
                {draft.scope === 'template' ? '模板' : '设备'}
                <Select value={draft.ownerId || 'none'} onValueChange={(ownerId) => setDraft({ ...draft, ownerId: ownerId === 'none' ? '' : ownerId })} disabled={!writable}>
                  <SelectTrigger><SelectValue placeholder='选择' /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value='none'>请选择</SelectItem>
                    {owners.map((item) => (
                      <SelectItem key={item.id} value={item.id}>{item.label}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </label>
              <label className='grid gap-1 text-xs text-muted-foreground'>
                单位
                <Input value={draft.unit} disabled={!writable} onChange={(event) => setDraft({ ...draft, unit: event.target.value })} />
              </label>
              <label className='grid gap-1 text-xs text-muted-foreground'>
                启用
                <Select value={draft.enabled ? 'yes' : 'no'} onValueChange={(value) => setDraft({ ...draft, enabled: value === 'yes' })} disabled={!writable}>
                  <SelectTrigger><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value='yes'>启用</SelectItem>
                    <SelectItem value='no'>停用</SelectItem>
                  </SelectContent>
                </Select>
              </label>
            </div>
            <Textarea
              className='min-h-28 font-mono text-sm'
              value={draft.expression}
              disabled={!writable}
              placeholder='if(spindleLoad > 80, 1, 0)'
              onChange={(event) => setDraft({ ...draft, expression: event.target.value })}
            />
            <div className='flex flex-wrap items-end gap-2'>
              <label className='grid gap-1 text-xs text-muted-foreground'>
                用这台设备的当前值预览
                <Select value={previewDevice || 'none'} onValueChange={(value) => setPreviewDevice(value === 'none' ? '' : value)}>
                  <SelectTrigger className='w-56'><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value='none'>只校验，不代入点位</SelectItem>
                    {devices.map((device) => (
                      <SelectItem key={device.metadata.id} value={device.metadata.id}>
                        {device.metadata.displayName || device.metadata.id}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </label>
              <Button type='button' variant='outline' onClick={() => void runPreview()}>实时预览</Button>
              <Button type='button' disabled={!writable} onClick={() => void save()}>保存</Button>
              <Button type='button' variant='ghost' disabled={!writable} onClick={() => { setDraft(blank()); setPreview(null) }}>新建</Button>
            </div>
            <div className='rounded-md border bg-muted/40 p-3' data-testid='computed-preview'>
              <p className='text-xs text-muted-foreground'>预览结果</p>
              <p className='text-2xl font-semibold'>{previewText}</p>
              {preview?.references?.length ? (
                <p className='text-xs text-muted-foreground'>引用点位：{preview.references.join('、')}</p>
              ) : null}
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>可用函数</CardTitle>
          </CardHeader>
          <CardContent className='grid gap-2 text-sm'>
            {help.map((item) => (
              <button
                key={item.name}
                type='button'
                className='rounded-md border px-3 py-2 text-left hover:bg-muted'
                onClick={() => setDraft({ ...draft, expression: draft.expression ? `${draft.expression} ${item.name.split(' ')[0]}` : item.name.split(' ')[0] })}
              >
                <span className='font-mono text-xs'>{item.name}</span>
                <span className='mt-1 block text-muted-foreground'>{item.text}</span>
              </button>
            ))}
            <p className='text-xs text-muted-foreground'>支持四则运算、比较、并且 / 或者 / 非，以及中文别名。不能写分号、花括号或任意代码。</p>
          </CardContent>
        </Card>
      </div>
      <Card className='mt-4'>
        <CardHeader>
          <CardTitle>已定义</CardTitle>
        </CardHeader>
        <CardContent>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>名称</TableHead>
                <TableHead>点位</TableHead>
                <TableHead>范围</TableHead>
                <TableHead>表达式</TableHead>
                <TableHead />
              </TableRow>
            </TableHeader>
            <TableBody>
              {points.length === 0 ? (
                <TableRow><TableCell colSpan={5} className='text-muted-foreground'>还没有计算点</TableCell></TableRow>
              ) : points.map((point) => (
                <TableRow key={point.id}>
                  <TableCell>{point.name}{point.enabled ? '' : '（停用）'}</TableCell>
                  <TableCell className='font-mono text-xs'>{point.pointId}</TableCell>
                  <TableCell>{point.scope === 'template' ? '模板' : '设备'} {point.ownerId}</TableCell>
                  <TableCell className='max-w-xs truncate font-mono text-xs'>{point.expression}</TableCell>
                  <TableCell className='text-right'>
                    <Button variant='ghost' size='sm' onClick={() => { setDraft(point); setPreview(null) }}>编辑</Button>
                    <Button variant='ghost' size='sm' disabled={!writable} onClick={() => void remove(point.id)}>删除</Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
    </PageShell>
  )
}
