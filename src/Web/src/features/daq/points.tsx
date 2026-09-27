import { useEffect, useRef, useState } from 'react'
import { Plus } from 'lucide-react'
import { toast } from 'sonner'
import { ConfirmDialog } from '@/components/confirm-dialog'
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
  canWrite,
  catalogAddress,
  describeError,
  normalizeDataType,
  studioApi,
  type CatalogBrand,
  type CatalogOverview,
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
  const [brands, setBrands] = useState<CatalogBrand[]>([])
  const [createOpen, setCreateOpen] = useState(false)
  const [newId, setNewId] = useState('')
  const [newName, setNewName] = useState('')
  const [newBrand, setNewBrand] = useState('fanuc')
  const [message, setMessage] = useState('')
  const [confirmRemove, setConfirmRemove] = useState(false)

  const template = templates.find((item) => item.metadata.id === templateId)
  const users = devices.filter((device) => device.spec.pointTemplateId === templateId)
  const templateBrand = brands.find((brand) => brand.id === (template?.spec.adapter || ''))
  const createBrand = brands.find((brand) => brand.id === newBrand) ?? brands[0]
  const catalog = templateBrand ? brandCatalog(templateBrand) : null

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
    void studioApi<CatalogOverview>('/api/v1/catalog/brands')
      .then((overview) => {
        if (cancelled) return
        setBrands(overview.brands)
        if (overview.brands[0] && !overview.brands.some((brand) => brand.id === 'fanuc')) {
          setNewBrand(overview.brands[0].id)
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) setMessage(describeError(error))
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
      const text = `点位 Id 不在${templateBrand?.nameZh ?? '该品牌'}目录中：${unknown.join('、')}。只能从该品牌目录选择。请移除后再保存。`
      setMessage(text)
      toast.error(text)
      return
    }
    const body: PointTemplateDocument = {
      metadata: { id: templateId, displayName: displayName.trim() },
      spec: {
        adapter: templateBrand?.id || template?.spec.adapter || 'fanuc',
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
    if (!createBrand) return
    const created = brandCatalog(createBrand)
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
        adapter: createBrand.id,
        points: created.points.map(fromCatalog),
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
    setMessage(`新模板已写入草稿，并带上${createBrand?.nameZh ?? ''}目录中的点位。按需要改启用后再发布。`)
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
    setMessage(`已从${templateBrand?.nameZh ?? '品牌'}目录加入尚未在表中的点位，尚未保存。`)
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
    if (!catalog || !templateBrand) {
      const text = '请先选择一个品牌模板，再按该品牌目录导入 CSV。不能手填协议地址。'
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
      const error = `CSV 中的点位 Id 不在${templateBrand.nameZh}目录中：${unique.join('、')}。只能导入该品牌目录里的点。`
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
    setMessage(`已按${templateBrand.nameZh}目录填入模板。地址列已忽略。确认后点「保存草稿」。`)
  }

  const missing =
    catalog?.points.filter((entry) => !points.some((point) => point.id.trim().toLowerCase() === entry.id.toLowerCase())) ??
    []
  const groups = groupByBrand(templates, brands)
  const description = catalog
    ? `${catalog.message} 一类模板给多台同类设备用。可改启用、单位、倍率和死区。Excel 请另存为 CSV。`
    : '正在读取品牌目录… 左侧按品牌分组。添加点位只能从该品牌目录里选。'

  return (
    <PageShell
      title='点位模板'
      description={description}
      fluid
      className='px-3 py-4'
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
      <div className='grid w-full min-w-0 items-start gap-2 lg:grid-cols-[12rem_minmax(0,1fr)]'>
        <Card className='gap-0 overflow-hidden py-0'>
          <div className='flex items-center justify-between gap-2 border-b px-2 py-1.5'>
            <CardTitle className='text-sm'>模板</CardTitle>
            <Button
              type='button'
              variant='ghost'
              size='icon'
              className='size-7'
              aria-label='新建模板'
              title='新建模板'
              disabled={!writable || brands.length === 0}
              onClick={() => setCreateOpen(true)}
            >
              <Plus />
            </Button>
          </div>
          <ScrollArea className='h-64 lg:h-[min(36rem,calc(100vh-12rem))]'>
            <nav aria-label='点位模板' className='grid gap-2 p-1.5'>
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
        </Card>

        <div className='grid min-w-0 gap-3'>
          <Card className='min-w-0 gap-4 py-4'>
            <CardHeader className='px-4'>
              <CardTitle>模板点表</CardTitle>
            </CardHeader>
            <CardContent className='min-w-0 px-4'>
              {!templateId ? (
                <div className='grid gap-3'>
                  <p className='text-sm text-muted-foreground'>
                    {templates.length === 0
                      ? '还没有点位模板。点「新建模板」，选择品牌后从该品牌目录生成。'
                      : '从左侧选择一个模板。'}
                  </p>
                  {message ? <p className='text-sm text-muted-foreground'>{message}</p> : null}
                </div>
              ) : (
                <div className='grid gap-3'>
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
                    品牌 {templateBrand?.nameZh ?? template?.spec.adapter}。内部地址由目录填写，采集按点位 id。不能手填协议地址。
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
                                : '正在读取品牌目录…'}
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
                                  ? '正在读取品牌目录…'
                                  : entry
                                    ? entry.description
                                    : `不在${templateBrand?.nameZh ?? '该品牌'}目录中。请移除，否则无法发布。`}
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
                      disabled={!writable || !templateId}
                      onClick={() => setConfirmRemove(true)}
                    >
                      删除模板
                    </Button>
                    {message ? <p className='text-sm text-muted-foreground'>{message}</p> : null}
                  </div>
                </div>
              )}
            </CardContent>
          </Card>

          <Card className='min-w-0 gap-4 py-4'>
            <CardHeader className='px-4'>
              <CardTitle>使用此模板的设备</CardTitle>
            </CardHeader>
            <CardContent className='px-4'>
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
              选择品牌后，模板会带上该品牌目录中的全部数据项。可以再关掉不需要的。不能手填协议地址。
            </DialogDescription>
          </DialogHeader>
          <form
            className='grid gap-4'
            onSubmit={(event) => {
              event.preventDefault()
              void createTemplate().catch(fail)
            }}
          >
            <div className='grid gap-1.5'>
              <Label>品牌</Label>
              <Select value={createBrand?.id} disabled={!writable} onValueChange={setNewBrand}>
                <SelectTrigger>
                  <SelectValue placeholder='选择品牌' />
                </SelectTrigger>
                <SelectContent>
                  {brands.map((brand) => (
                    <SelectItem key={brand.id} value={brand.id}>
                      {brand.nameZh}（{brand.nameEn}）
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
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
              <Button type='submit' disabled={!writable || !createBrand || !newId.trim()}>
                新建模板
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
      <ConfirmDialog
        open={confirmRemove}
        onOpenChange={setConfirmRemove}
        title='删除点位模板'
        desc={`从草稿删除模板「${displayName || template?.metadata.displayName || templateId}」。仍引用它的设备在发布时会校验失败。`}
        destructive
        confirmText='删除'
        handleConfirm={() => {
          setConfirmRemove(false)
          void removeTemplate().catch(fail)
        }}
      />
    </PageShell>
  )
}

function groupByBrand(templates: PointTemplateDocument[], brands: CatalogBrand[]) {
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
    const leftName = brands.find((brand) => brand.id === left)?.nameZh ?? left
    const rightName = brands.find((brand) => brand.id === right)?.nameZh ?? right
    return leftName.localeCompare(rightName, 'zh')
  })
  return order.map((adapter) => ({
    adapter: adapter || 'ungrouped',
    label: brands.find((brand) => brand.id === adapter)?.nameZh || (adapter === 'fanuc' ? '发那科' : adapter || '未分组'),
    templates: groups.get(adapter) ?? [],
  }))
}

function brandCatalog(brand: CatalogBrand): PointCatalogDocument {
  return {
    adapter: brand.id,
    scope: brand.nameZh,
    message: `${brand.nameZh}（${brand.nameEn}）共 ${brand.items.length} 个目录项。只能从该品牌目录选择，不能手填协议地址。`,
    points: brand.items.map((item) => ({
      id: item.id,
      dataType: normalizeDataType(item.dataType),
      description: item.nameZh,
      scale: 1,
      deadband: 0,
      address: catalogAddress(brand.id, item.id),
      unit: item.unit,
    })),
  }
}

function fromCatalog(entry: PointCatalogEntry): PointDefinition {
  return {
    id: entry.id,
    address: entry.address,
    dataType: entry.dataType,
    unit: entry.unit ?? '',
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
