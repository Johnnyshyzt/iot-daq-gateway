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
  normalizeDataType,
  pointDataTypes,
  studioApi,
  type DeviceDocument,
  type PointDefinition,
  type PointSetDocument,
} from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'
import { PageShell } from './page-shell'

const blank = (): PointDefinition => ({
  id: '',
  address: 'cnc/statinfo',
  dataType: 'string',
  unit: '',
  scale: 1,
  deadband: 0,
  enabled: true,
})

const csvHeaders = ['id', 'address', 'dataType', 'unit', 'scale', 'deadband', 'enabled'] as const

export function PointsPage() {
  const writable = canWrite(useAuthStore((state) => state.auth.user?.role[0]))
  const fileRef = useRef<HTMLInputElement>(null)
  const [devices, setDevices] = useState<DeviceDocument[]>([])
  const [deviceId, setDeviceId] = useState('')
  const [points, setPoints] = useState<PointDefinition[]>([])
  const [message, setMessage] = useState('')

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
    void studioApi<PointSetDocument>(`/api/v1/config/points/${encodeURIComponent(deviceId)}`)
      .then((document) => {
        setPoints(document.spec.points.map((point) => ({ ...point, dataType: normalizeDataType(point.dataType) })))
        setMessage('')
      })
      .catch((error: unknown) => setMessage(describeError(error)))
  }, [deviceId])

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

  async function save() {
    const body: PointSetDocument = {
      metadata: { deviceId },
      spec: {
        points: points
          .filter((point) => point.id.trim())
          .map((point) => ({ ...point, id: point.id.trim(), dataType: normalizeDataType(point.dataType) })),
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

  function exportCsv() {
    const lines = [csvHeaders.join(',')]
    for (const point of points) {
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
    const addressAt = named(['address', '地址']) >= 0 ? named(['address', '地址']) : 1
    const typeAt = named(['datatype', '类型']) >= 0 ? named(['datatype', '类型']) : 2
    const unitAt = named(['unit', '单位']) >= 0 ? named(['unit', '单位']) : 3
    const scaleAt = named(['scale', '倍率'])
    const deadbandAt = named(['deadband', '死区'])
    const enabledAt = named(['enabled', '启用']) >= 0 ? named(['enabled', '启用']) : legacy ? 4 : 6
    setPoints(
      body.map((row) => ({
        ...blank(),
        id: row[idAt]?.trim() ?? '',
        address: row[addressAt]?.trim() || 'cnc/statinfo',
        dataType: normalizeDataType(row[typeAt]?.trim() || 'string'),
        unit: row[unitAt]?.trim() ?? '',
        scale: numberOr(row[scaleAt], 1),
        deadband: numberOr(row[deadbandAt], 0),
        enabled: (row[enabledAt] ?? 'true').trim().toLowerCase() !== 'false',
      }))
    )
    setMessage('已从 CSV 填入表格，尚未保存。确认后点「保存草稿」。')
  }

  return (
    <PageShell
      title='点位'
      description='按设备编辑整份点表。Excel 请另存为 CSV 再导入。示例地址：cnc/statinfo、cnc/alarm、cnc/program。'
      actions={
        <div className='flex gap-2'>
          <Button variant='outline' onClick={exportCsv} disabled={!deviceId}>
            导出 CSV
          </Button>
          <Button
            variant='outline'
            disabled={!writable || !deviceId}
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
            variant='outline'
            disabled={!writable || !deviceId}
            onClick={() => setPoints((current) => [...current, blank()])}
          >
            添加点位
          </Button>
        </div>
      }
    >
      <Card>
        <CardHeader className='flex flex-row items-center justify-between gap-3'>
          <CardTitle>点表</CardTitle>
          {devices.length > 0 ? (
            <Select value={deviceId} onValueChange={setDeviceId}>
              <SelectTrigger className='w-56'>
                <SelectValue placeholder='选择设备' />
              </SelectTrigger>
              <SelectContent>
                {devices.map((device) => (
                  <SelectItem key={device.metadata.id} value={device.metadata.id}>
                    {device.metadata.displayName || device.metadata.id}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          ) : null}
        </CardHeader>
        <CardContent>
          <div className='overflow-auto'>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>点位 Id</TableHead>
                  <TableHead>地址</TableHead>
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
                      {deviceId ? '这台设备还没有点位。' : '请先添加设备。'}
                    </TableCell>
                  </TableRow>
                ) : null}
                {points.map((point, index) => (
                  <TableRow key={`${point.id}-${index}`}>
                    <TableCell>
                      <Input
                        value={point.id}
                        disabled={!writable}
                        onChange={(event) => update(index, { id: event.target.value })}
                      />
                    </TableCell>
                    <TableCell>
                      <Input
                        value={point.address}
                        disabled={!writable}
                        onChange={(event) => update(index, { address: event.target.value })}
                      />
                    </TableCell>
                    <TableCell>
                      <Select
                        value={normalizeDataType(point.dataType)}
                        disabled={!writable}
                        onValueChange={(dataType) => update(index, { dataType })}
                      >
                        <SelectTrigger className='w-36'>
                          <SelectValue />
                        </SelectTrigger>
                        <SelectContent>
                          {pointDataTypes.map((item) => (
                            <SelectItem key={item.value} value={item.value}>
                              {item.label}
                            </SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    </TableCell>
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
                ))}
              </TableBody>
            </Table>
          </div>
          <div className='mt-4 flex items-center gap-3'>
            <Button
              disabled={!writable || !deviceId}
              onClick={() => void save().catch(fail)}
            >
              保存草稿
            </Button>
            {message ? <p className='text-sm text-muted-foreground'>{message}</p> : null}
          </div>
        </CardContent>
      </Card>
    </PageShell>
  )
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
