import { useEffect, useRef, useState } from 'react'
import { toast } from 'sonner'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { ScrollArea } from '@/components/ui/scroll-area'
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
  canWrite,
  describeError,
  normalizeDataType,
  studioApi,
  type DeviceDocument,
  type PointCatalogDocument,
  type PointCatalogEntry,
  type PointDefinition,
  type PointTemplateDocument,
} from '@/lib/studio-api'
import { cn } from '@/lib/utils'
import { useAuthStore } from '@/stores/auth-store'
import { PageShell } from './page-shell'

const csvHeaders = ['id', 'address', 'dataType', 'unit', 'scale', 'deadband', 'enabled'] as const
const idPattern = /^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$/

export function PointsPage() {
  const writable = canWrite(useAuthStore((state) => state.auth.user?.role[0]))
  const fileRef = useRef<HTMLInputElement>(null)
  const nameRef = useRef<HTMLInputElement>(null)
  const focusName = useRef(false)
  const [templates, setTemplates] = useState<PointTemplateDocument[]>([])
  const [devices, setDevices] = useState<DeviceDocument[]>([])
  const [templateId, setTemplateId] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [points, setPoints] = useState<PointDefinition[]>([])
  const [catalog, setCatalog] = useState<PointCatalogDocument | null>(null)
  const [createOpen, setCreateOpen] = useState(false)
  const [newId, setNewId] = useState('')
  const [newName, setNewName] = useState('')
  const [message, setMessage] = useState('')

  const template = templates.find((item) => item.metadata.id === templateId)
  const users = devices.filter((device) => device.spec.pointTemplateId === templateId)

  async function reload() {
    const [templateItems, deviceItems] = await Promise.all([
      studioApi<PointTemplateDocument[]>('/api/v1/config/point-templates'),
      studioApi<DeviceDocument[]>('/api/v1/config/devices'),
    ])
    setTemplates(templateItems)
    setDevices(deviceItems)
    return templateItems
  }

  useEffect(() => {
    void reload()
      .then((items) => {
        if (items[0]) setTemplateId(items[0].metadata.id)
      })
      .catch((error: unknown) => setMessage(describeError(error)))
  }, [])

  useEffect(() => {
    if (!templateId) {
      setDisplayName('')
      setPoints([])
      return
    }
    let cancelled = false
    void studioApi<PointTemplateDocument>(`/api/v1/config/point-templates/${encodeURIComponent(templateId)}`)
      .then((document) => {
        if (cancelled) return
        setDisplayName(document.metadata.displayName)
        setPoints(document.spec.points.map((point) => ({ ...point, dataType: normalizeDataType(point.dataType) })))
      })
      .catch((error: unknown) => {
        if (!cancelled) setMessage(describeError(error))
      })
    return () => {
      cancelled = true
    }
  }, [templateId])

  useEffect(() => {
    let cancelled = false
    void studioApi<PointCatalogDocument>('/api/v1/catalog/points?adapter=fanuc.fake')
      .then((document) => {
        if (!cancelled) setCatalog(document)
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          setCatalog(null)
          setMessage(describeError(error))
        }
      })
    return () => {
      cancelled = true
    }
  }, [])

  function entryFor(id: string) {
    const key = id.trim().toLowerCase()
    return catalog?.points.find((item) => item.id.toLowerCase() === key)
  }

  function update(index: number, patch: Partial<PointDefinition>) {
    setPoints((current) =>
      current.map((point, i) => (i === index ? { ...point, ...patch } : point))
    )
  }

  function fail(error: unknown) {
    const text = describeError(error)
    setMessage(text)
    toast.error(text)
  }

  function withCatalog(point: PointDefinition): PointDefinition {
    const entry = entryFor(point.id)
    if (!entry) {
      return { ...point, id: point.id.trim(), dataType: normalizeDataType(point.dataType) }
    }
    return {
      ...point,
      id: entry.id,
      address: entry.address,
      dataType: entry.dataType,
    }
  }

  async function save() {
    if (!templateId || !catalog) return
    if (!displayName.trim()) {
      const text = '请填写模板显示名称'
      setMessage(text)
      toast.error(text)
      return
    }
    const unknown = points
      .map((point) => point.id.trim())
      .filter((id) => id && !entryFor(id))
    if (unknown.length > 0) {
      const text = `点位 Id 不在发那科目录中：${unknown.join('、')}。只能使用 ${catalog.points.map((item) => item.id).join('、')}。请移除后再保存。`
      setMessage(text)
      toast.error(text)
      return
    }
    const body: PointTemplateDocument = {
      metadata: { id: templateId, displayName: displayName.trim() },
      spec: {
        adapter: 'fanuc',
        points: points.filter((point) => point.id.trim()).map(withCatalog),
      },
    }
    await studioApi(`/api/v1/config/point-templates/${encodeURIComponent(templateId)}`, {
      method: 'PUT',
      body: JSON.stringify(body),
    })
    setPoints(body.spec.points)
    await reload()
    setMessage('模板已写入草稿。引用它的设备要等发布后才会按这份点表采集。')
    toast.success('点位模板已保存')
  }

  async function createTemplate() {
    if (!catalog) return
    const id = newId.trim()
    if (!idPattern.test(id)) {
      const text = '模板 Id 只能包含字母、数字、下划线和连字符，且必须以字母或数字开头'
      setMessage(text)
      toast.error(text)
      return
    }
    const body: PointTemplateDocument = {
      metadata: { id, displayName: newName.trim() || id },
      spec: {
        adapter: 'fanuc',
        points: catalog.points.map(fromCatalog),
      },
    }
    await studioApi(`/api/v1/config/point-templates/${encodeURIComponent(id)}`, {
      method: 'PUT',
      body: JSON.stringify(body),
    })
    setNewId('')
    setNewName('')
    focusName.current = true
    await reload()
    setTemplateId(id)
    setCreateOpen(false)
    setMessage('新模板已写入草稿，并带上发那科目录里的三个点。按需要改启用后再发布。')
    toast.success('已新建点位模板')
  }

  async function removeTemplate() {
    if (!templateId) return
    const removed = templateId
    const index = templates.findIndex((item) => item.metadata.id === removed)
    await studioApi(`/api/v1/config/point-templates/${encodeURIComponent(removed)}`, {
      method: 'DELETE',
    })
    const items = await reload()
    const next = items[index] ?? items[index - 1] ?? items[0]
    setTemplateId(next?.metadata.id ?? '')
    setMessage('模板已从草稿删除。')
    toast.success('点位模板已删除')
  }

  function selectTemplate(id: string) {
    setTemplateId(id)
    setMessage('')
  }

  function closeCreate(open: boolean) {
    setCreateOpen(open)
    if (!open) {
      setNewId('')
      setNewName('')
    }
  }

  function addFromCatalog() {
    if (!catalog) return
    setPoints((current) => {
      const have = new Set(current.map((point) => point.id.trim().toLowerCase()))
      const added = catalog.points
        .filter((entry) => !have.has(entry.id.toLowerCase()))
        .map(fromCatalog)
      return added.length === 0 ? current : [...current, ...added]
    })
    setMessage('已从发那科目录加入尚未在表中的点位，尚未保存。')
  }

  function exportCsv() {
    const lines = [csvHeaders.join(',')]
    for (const point of points.map(withCatalog)) {
      lines.push(
        [
          point.id,
          point.address,
          point.dataType,
          point.unit,
          String(point.scale),
          String(point.deadband),
          point.enabled ? 'true' : 'false',
        ]
          .map(csvCell)
          .join(',')
      )
    }
    const blob = new Blob([lines.join('\n')], { type: 'text/csv;charset=utf-8' })
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = `${templateId || 'point-template'}.csv`
    link.click()
    URL.revokeObjectURL(url)
  }

  async function importCsv(file: File) {
    if (!catalog) {
      const text = 'M1 只支持发那科点位目录，不能按通用地址导入 CSV。'
      setMessage(text)
      toast.error(text)
      return
    }
    const text = await file.text()
    const rows = parseCsv(text).filter((row) => row.some((cell) => cell.trim()))
    if (rows.length === 0) {
      setMessage('CSV 是空的')
      return
    }
    const header = rows[0].map((cell) => cell.trim().toLowerCase())
    const hasHeader = header.includes('id') || header.includes('address') || header.includes('点位')
    const body = hasHeader ? rows.slice(1) : rows
    const named = (names: string[]) => (hasHeader ? header.findIndex((cell) => names.includes(cell)) : -1)
    const legacy = !hasHeader || named(['scale', '倍率']) < 0
    const idAt = named(['id', '点位', '点位id']) >= 0 ? named(['id', '点位', '点位id']) : 0
    const unitAt = named(['unit', '单位']) >= 0 ? named(['unit', '单位']) : 3
    const scaleAt = named(['scale', '倍率'])
    const deadbandAt = named(['deadband', '死区'])
    const enabledAt = named(['enabled', '启用']) >= 0 ? named(['enabled', '启用']) : legacy ? 4 : 6
    const ids = body.map((row) => row[idAt]?.trim() ?? '').filter((id) => id)
    const unknown = ids.filter((id) => !entryFor(id))
    if (unknown.length > 0) {
      const unique = [...new Set(unknown)]
      const error = `CSV 中的点位 Id 不在发那科目录中：${unique.join('、')}。只能导入 ${catalog.points.map((item) => item.id).join('、')}。这不是跨品牌地址表。`
      setMessage(error)
      toast.error(error)
      return
    }
    const seen = new Set<string>()
    const duplicates = ids.filter((id) => {
      const key = entryFor(id)?.id ?? id
      if (seen.has(key)) return true
      seen.add(key)
      return false
    })
    if (duplicates.length > 0) {
      const error = `CSV 里点位 Id 重复：${[...new Set(duplicates)].join('、')}。`
      setMessage(error)
      toast.error(error)
      return
    }
    setPoints(
      body
        .filter((row) => (row[idAt]?.trim() ?? '') !== '')
        .map((row) => {
          const entry = entryFor(row[idAt]?.trim() ?? '') as PointCatalogEntry
          return {
            ...fromCatalog(entry),
            unit: row[unitAt]?.trim() ?? '',
            scale: numberOr(row[scaleAt], entry.scale),
            deadband: numberOr(row[deadbandAt], entry.deadband),
            enabled: (row[enabledAt] ?? 'true').trim().toLowerCase() !== 'false',
          }
        })
    )
    setMessage('已按发那科目录填入模板。地址列已忽略。确认后点「保存草稿」。')
  }

  const missing =
    catalog?.points.filter((entry) => !points.some((point) => point.id.trim().toLowerCase() === entry.id.toLowerCase())) ??
    []
  const groups = groupByAdapter(templates)
  const description = catalog
    ? `${catalog.message} 一类模板给多台同类设备用，不是每台各写一张地址表。可改启用、单位、倍率和死区。Excel 请另存为 CSV。`
    : '正在读取发那科点位目录… 一类模板给多台同类设备用，不是每台各写一张地址表。'

  return (
    <PageShell
      title='点位模板'
      description={description}
      actions={
        <div className='flex flex-wrap gap-2'>
          <Button variant='outline' onClick={exportCsv} disabled={!templateId}>
            导出 CSV
          </Button>
          <Button
            variant='outline'
            disabled={!writable || !templateId || !catalog}
            onClick={() => fileRef.current?.click()}
          >
            导入 CSV
          </Button>
          <input
            ref={fileRef}
            type='file'
            accept='.csv,text/csv'
            className='hidden'
            onChange={(event) => {
              const file = event.target.files?.[0]
              event.target.value = ''
              if (file) void importCsv(file).catch(fail)
            }}
          />
          <Button
            disabled={!writable || !catalog || !templateId || missing.length === 0}
            title={missing.length === 0 ? '目录中的点位都已在表中' : '加入目录里还没有的点位'}
            onClick={addFromCatalog}
          >
            从目录添加
          </Button>
        </div>
      }
    >
      <div className='grid min-w-0 items-start gap-4 lg:grid-cols-[16rem_minmax(0,1fr)]'>
        <Card className='gap-0 overflow-hidden py-0'>
          <div className='border-b px-4 py-3'>
            <CardTitle className='text-base'>模板</CardTitle>
          </div>
          <ScrollArea className='h-64 lg:h-[min(36rem,calc(100vh-14rem))]'>
            <nav aria-label='点位模板' className='grid gap-4 p-2'>
              {groups.length === 0 ? (
                <p className='px-2 py-3 text-sm text-muted-foreground'>还没有点位模板。</p>
              ) : (
                groups.map((group) => (
                  <div key={group.adapter} className='grid gap-0.5'>
                    <p className='px-2 py-1 text-xs font-medium text-muted-foreground'>{group.label}</p>
                    {group.templates.map((item) => {
                      const active = item.metadata.id === templateId
                      return (
                        <button
                          key={item.metadata.id}
                          type='button'
                          aria-current={active ? 'true' : undefined}
                          className={cn(
                            'rounded-md px-2 py-2 text-start text-sm hover:bg-accent',
                            active && 'bg-accent font-medium text-accent-foreground'
                          )}
                          onClick={() => selectTemplate(item.metadata.id)}
                        >
                          <span className='block truncate'>
                            {item.metadata.displayName || item.metadata.id}
                          </span>
                          <span className='block truncate font-mono text-xs text-muted-foreground'>
                            {item.metadata.id}
                          </span>
                        </button>
                      )
                    })}
                  </div>
                ))
              )}
            </nav>
          </ScrollArea>
          <div className='border-t p-3'>
            <Button
              className='w-full'
              disabled={!writable || !catalog}
              onClick={() => setCreateOpen(true)}
            >
              新建模板
            </Button>
          </div>
        </Card>

        <div className='grid min-w-0 gap-4'>
          <Card className='min-w-0'>
            <CardHeader>
              <CardTitle>模板点表</CardTitle>
            </CardHeader>
            <CardContent className='min-w-0'>
              {!templateId ? (
                <div className='grid gap-3'>
                  <p className='text-sm text-muted-foreground'>
                    {templates.length === 0
                      ? '还没有点位模板。点「新建模板」创建一份发那科模板。'
                      : '从左侧选择一个模板。'}
                  </p>
                  {message ? <p className='text-sm text-muted-foreground'>{message}</p> : null}
                </div>
              ) : (
                <div className='grid gap-4'>
                  <div className='grid max-w-md gap-1.5'>
                    <Label htmlFor='template-display-name'>显示名称</Label>
                    <Input
                      id='template-display-name'
                      ref={nameRef}
                      value={displayName}
                      disabled={!writable}
                      onChange={(event) => setDisplayName(event.target.value)}
                    />
                  </div>
                  <p className='text-xs text-muted-foreground'>
                    适配器族 fanuc，供 fanuc.fake 与 fanuc.focas 共用。内部地址由目录填写，采集按点位 id。
                  </p>
                  <div className='overflow-auto'>
                    <Table>
                      <TableHeader>
                        <TableRow>
                          <TableHead>点位 Id</TableHead>
                          <TableHead>采集方式</TableHead>
                          <TableHead>类型</TableHead>
                          <TableHead>单位</TableHead>
                          <TableHead>倍率</TableHead>
                          <TableHead>死区</TableHead>
                          <TableHead>启用</TableHead>
                          <TableHead />
                        </TableRow>
                      </TableHeader>
                      <TableBody>
                        {points.length === 0 ? (
                          <TableRow>
                            <TableCell colSpan={8} className='text-muted-foreground'>
                              {catalog
                                ? `模板里还没有点位。点「从目录添加」加入 ${catalog.points.map((item) => item.id).join('、')}。`
                                : '正在读取发那科点位目录…'}
                            </TableCell>
                          </TableRow>
                        ) : null}
                        {points.map((point, index) => {
                          const entry = entryFor(point.id)
                          return (
                            <TableRow key={`${point.id}-${index}`}>
                              <TableCell className='font-mono text-sm'>{point.id || '—'}</TableCell>
                              <TableCell className='max-w-64 whitespace-normal text-sm text-muted-foreground'>
                                {!catalog
                                  ? '正在读取发那科点位目录…'
                                  : entry
                                    ? entry.description
                                    : '不在发那科目录中。请移除，否则无法发布。'}
                              </TableCell>
                              <TableCell className='text-sm'>{entry?.dataType ?? normalizeDataType(point.dataType)}</TableCell>
                              <TableCell>
                                <Input
                                  value={point.unit}
                                  disabled={!writable}
                                  onChange={(event) => update(index, { unit: event.target.value })}
                                />
                              </TableCell>
                              <TableCell>
                                <Input
                                  type='number'
                                  className='w-24'
                                  value={point.scale}
                                  disabled={!writable}
                                  onChange={(event) => update(index, { scale: Number(event.target.value) })}
                                />
                              </TableCell>
                              <TableCell>
                                <Input
                                  type='number'
                                  className='w-24'
                                  value={point.deadband}
                                  disabled={!writable}
                                  onChange={(event) => update(index, { deadband: Number(event.target.value) })}
                                />
                              </TableCell>
                              <TableCell>
                                <Switch
                                  checked={point.enabled}
                                  disabled={!writable}
                                  onCheckedChange={(enabled) => update(index, { enabled })}
                                />
                              </TableCell>
                              <TableCell>
                                <Button
                                  size='sm'
                                  variant='ghost'
                                  disabled={!writable}
                                  onClick={() => setPoints((current) => current.filter((_, i) => i !== index))}
                                >
                                  移除
                                </Button>
                              </TableCell>
                            </TableRow>
                          )
                        })}
                      </TableBody>
                    </Table>
                  </div>
                  <div className='flex flex-wrap items-center gap-3'>
                    <Button disabled={!writable || !catalog} onClick={() => void save().catch(fail)}>
                      保存草稿
                    </Button>
                    <Button
                      variant='destructive'
                      disabled={!writable}
                      onClick={() => void removeTemplate().catch(fail)}
                    >
                      删除模板
                    </Button>
                    {message ? <p className='text-sm text-muted-foreground'>{message}</p> : null}
                  </div>
                </div>
              )}
            </CardContent>
          </Card>

          <Card className='min-w-0'>
            <CardHeader>
              <CardTitle>使用此模板的设备</CardTitle>
            </CardHeader>
            <CardContent>
              {!templateId ? (
                <p className='text-sm text-muted-foreground'>选择模板后，这里列出引用它的设备。设备页只选择模板，不重填点位。</p>
              ) : users.length === 0 ? (
                <p className='text-sm text-muted-foreground'>还没有设备引用「{displayName || template?.metadata.displayName || templateId}」。</p>
              ) : (
                <div className='flex flex-wrap gap-2'>
                  {users.map((device) => (
                    <Badge key={device.metadata.id} variant='secondary'>
                      {(device.metadata.displayName || device.metadata.id) + ` · ${device.metadata.id} · ${device.spec.adapter}`}
                    </Badge>
                  ))}
                </div>
              )}
            </CardContent>
          </Card>
        </div>
      </div>

      <Dialog open={createOpen} onOpenChange={closeCreate}>
        <DialogContent
          onCloseAutoFocus={(event) => {
            if (!focusName.current) return
            focusName.current = false
            event.preventDefault()
            nameRef.current?.focus()
          }}
        >
          <DialogHeader>
            <DialogTitle>新建模板</DialogTitle>
            <DialogDescription>
              新建的是另一类发那科设备的点表，例如只启用报警。创建后带上目录中的三个点，可再关掉不需要的。
            </DialogDescription>
          </DialogHeader>
          <form
            className='grid gap-4'
            onSubmit={(event) => {
              event.preventDefault()
              void createTemplate().catch(fail)
            }}
          >
            <div className='grid gap-3 sm:grid-cols-2'>
              <div className='grid gap-1.5'>
                <Label htmlFor='new-template-id'>模板 Id</Label>
                <Input
                  id='new-template-id'
                  value={newId}
                  disabled={!writable}
                  placeholder='fanuc-alarm-only'
                  autoFocus
                  onChange={(event) => setNewId(event.target.value)}
                />
              </div>
              <div className='grid gap-1.5'>
                <Label htmlFor='new-template-name'>显示名称</Label>
                <Input
                  id='new-template-name'
                  value={newName}
                  disabled={!writable}
                  placeholder='Fanuc 只看报警'
                  onChange={(event) => setNewName(event.target.value)}
                />
              </div>
            </div>
            <DialogFooter>
              <Button type='button' variant='outline' onClick={() => closeCreate(false)}>
                取消
              </Button>
              <Button type='submit' disabled={!writable || !catalog || !newId.trim()}>
                新建模板
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </PageShell>
  )
}

function groupByAdapter(templates: PointTemplateDocument[]) {
  const order: string[] = []
  const groups = new Map<string, PointTemplateDocument[]>()
  for (const template of templates) {
    const adapter = template.spec.adapter?.trim() || ''
    const existing = groups.get(adapter)
    if (existing) existing.push(template)
    else {
      groups.set(adapter, [template])
      order.push(adapter)
    }
  }
  order.sort((left, right) => {
    if (left === 'fanuc') return -1
    if (right === 'fanuc') return 1
    return left.localeCompare(right)
  })
  return order.map((adapter) => ({
    adapter: adapter || 'ungrouped',
    label: adapter === 'fanuc' ? 'Fanuc' : adapter || '未分组',
    templates: groups.get(adapter) ?? [],
  }))
}

function fromCatalog(entry: PointCatalogEntry): PointDefinition {
  return {
    id: entry.id,
    address: entry.address,
    dataType: entry.dataType,
    unit: '',
    scale: entry.scale,
    deadband: entry.deadband,
    enabled: true,
  }
}

function numberOr(value: string | undefined, fallback: number) {
  if (value === undefined || value.trim() === '') return fallback
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : fallback
}

function csvCell(value: string) {
  if (/[",\n]/.test(value)) return `"${value.split('"').join('""')}"`
  return value
}

function parseCsv(text: string) {
  const rows: string[][] = []
  let row: string[] = []
  let cell = ''
  let quoted = false
  const source = text.replace(/^\uFEFF/, '')
  for (let i = 0; i < source.length; i++) {
    const char = source[i]
    if (quoted) {
      if (char === '"') {
        if (source[i + 1] === '"') {
          cell += '"'
          i++
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
  if (cell.length > 0 || row.length > 0) {
    row.push(cell)
    rows.push(row)
  }
  return rows
}
