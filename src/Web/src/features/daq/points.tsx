import { useEffect, useRef, useState } from 'react'
import { toast } from 'sonner'
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
  isFanucAdapter,
  normalizeDataType,
  studioApi,
  type DeviceDocument,
  type PointCatalogDocument,
  type PointCatalogEntry,
  type PointDefinition,
  type PointSetDocument,
} from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'
import { PageShell } from './page-shell'

const csvHeaders = ['id', 'address', 'dataType', 'unit', 'scale', 'deadband', 'enabled'] as const

export function PointsPage() {
  const writable = canWrite(useAuthStore((state) => state.auth.user?.role[0]))
  const fileRef = useRef<HTMLInputElement>(null)
  const [devices, setDevices] = useState<DeviceDocument[]>([])
  const [deviceId, setDeviceId] = useState('')
  const [points, setPoints] = useState<PointDefinition[]>([])
  const [catalog, setCatalog] = useState<PointCatalogDocument | null>(null)
  const [message, setMessage] = useState('')

  const device = devices.find((item) => item.metadata.id === deviceId)
  const adapter = device?.spec.adapter ?? ''
  const fanuc = isFanucAdapter(adapter)

  useEffect(() => {
    void studioApi<DeviceDocument[]>('/api/v1/config/devices')
      .then((items) => {
        setDevices(items)
        if (items[0]) setDeviceId(items[0].metadata.id)
        else setMessage('请先在设备页添加一台设备')
      })
      .catch((error: unknown) => setMessage(describeError(error)))
  }, [])

  useEffect(() => {
    if (!deviceId) return
    let cancelled = false
    void studioApi<PointSetDocument>(`/api/v1/config/points/${encodeURIComponent(deviceId)}`)
      .then((document) => {
        if (!cancelled) {
          setPoints(document.spec.points.map((point) => ({ ...point, dataType: normalizeDataType(point.dataType) })))
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) setMessage(describeError(error))
      })
    return () => {
      cancelled = true
    }
  }, [deviceId])

  useEffect(() => {
    if (!fanuc) return
    let cancelled = false
    void studioApi<PointCatalogDocument>(`/api/v1/catalog/points?adapter=${encodeURIComponent(adapter)}`)
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
  }, [adapter, fanuc])

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
    if (!fanuc || !catalog) return
    const unknown = points
      .map((point) => point.id.trim())
      .filter((id) => id && !entryFor(id))
    if (unknown.length > 0) {
      const text = `点位 Id 不在发那科目录中：${unknown.join('、')}。只能使用 ${catalog.points.map((item) => item.id).join('、')}。请移除后再保存。`
      setMessage(text)
      toast.error(text)
      return
    }
    const body: PointSetDocument = {
      metadata: { deviceId },
      spec: {
        points: points.filter((point) => point.id.trim()).map(withCatalog),
      },
    }
    await studioApi(`/api/v1/config/points/${encodeURIComponent(deviceId)}`, {
      method: 'PUT',
      body: JSON.stringify(body),
    })
    setPoints(body.spec.points)
    setMessage('点位已写入草稿。发布后采集才会使用这份点表。')
    toast.success('点位已保存')
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
    link.download = `${deviceId || 'points'}.csv`
    link.click()
    URL.revokeObjectURL(url)
  }

  async function importCsv(file: File) {
    if (!fanuc || !catalog) {
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
    setMessage('已按发那科目录填入表格。地址列已忽略。确认后点「保存草稿」。')
  }

  const missing =
    catalog?.points.filter((entry) => !points.some((point) => point.id.trim().toLowerCase() === entry.id.toLowerCase())) ??
    []
  const description = describePage(deviceId, fanuc, adapter, catalog)

  return (
    <PageShell
      title='点位'
      description={description}
      actions={
        fanuc ? (
          <div className='flex flex-wrap gap-2'>
            <Button variant='outline' onClick={exportCsv} disabled={!deviceId}>
              导出 CSV
            </Button>
            <Button
              variant='outline'
              disabled={!writable || !deviceId || !catalog}
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
              disabled={!writable || !catalog || missing.length === 0}
              title={missing.length === 0 ? '目录中的点位都已在表中' : '加入目录里还没有的点位'}
              onClick={addFromCatalog}
            >
              从目录添加
            </Button>
          </div>
        ) : null
      }
    >
      <Card>
        <CardHeader className='flex flex-row items-center justify-between gap-3'>
          <CardTitle>点表</CardTitle>
          {devices.length > 0 ? (
            <Select
              value={deviceId}
              onValueChange={(id) => {
                setDeviceId(id)
                setMessage('')
              }}
            >
              <SelectTrigger className='w-72'>
                <SelectValue placeholder='选择设备' />
              </SelectTrigger>
              <SelectContent>
                {devices.map((item) => (
                  <SelectItem key={item.metadata.id} value={item.metadata.id}>
                    {(item.metadata.displayName || item.metadata.id) + `（${item.spec.adapter}）`}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          ) : null}
        </CardHeader>
        <CardContent>
          {!deviceId ? (
            <p className='text-sm text-muted-foreground'>请先添加设备。</p>
          ) : !fanuc ? (
            <p className='text-sm text-muted-foreground'>
              M1 只支持发那科点位目录（fanuc.fake、fanuc.focas）。当前适配器是「{adapter || '未填写'}」，不能按通用地址编辑。请在设备页改回发那科适配器。
            </p>
          ) : (
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
                          ? `这台设备还没有点位。点「从目录添加」加入 ${catalog.points.map((item) => item.id).join('、')}。`
                          : '正在读取发那科点位目录…'}
                      </TableCell>
                    </TableRow>
                  ) : null}
                  {points.map((point, index) => {
                    const entry = entryFor(point.id)
                    return (
                      <TableRow key={`${point.id}-${index}`}>
                        <TableCell className='font-mono text-sm'>{point.id || '—'}</TableCell>
                        <TableCell className='max-w-64 text-sm text-muted-foreground'>
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
          )}
          {fanuc ? (
            <div className='mt-4 flex items-center gap-3'>
              <Button disabled={!writable || !deviceId || !catalog} onClick={() => void save().catch(fail)}>
                保存草稿
              </Button>
              {message ? <p className='text-sm text-muted-foreground'>{message}</p> : null}
            </div>
          ) : message ? (
            <p className='mt-4 text-sm text-muted-foreground'>{message}</p>
          ) : null}
        </CardContent>
      </Card>
    </PageShell>
  )
}

function describePage(
  deviceId: string,
  fanuc: boolean,
  adapter: string,
  catalog: PointCatalogDocument | null
) {
  if (!deviceId) return '请先在设备页添加一台设备。'
  if (!fanuc) {
    return 'M1 只支持发那科点位目录（fanuc.fake、fanuc.focas）。这里没有通用地址编辑器。'
  }
  const notice = catalog?.message ?? '正在读取发那科点位目录…'
  return `${notice} 当前适配器 ${adapter}。可改启用、单位、倍率和死区。Excel 请另存为 CSV。导入只接受目录中的点位 Id，地址列不会当成协议地址。`
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
