import { useEffect, useState } from 'react'
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
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
import { PageShell } from '@/features/daq/page-shell'
import { describeError, studioApi, type DeviceDocument } from '@/lib/studio-api'
import { downloadFile, formatMinutes, formatPercent } from './format'
import type { UtilizationResponse, UtilizationRow } from './types'

export function UtilizationPage() {
  const [devices, setDevices] = useState<DeviceDocument[]>([])
  const [deviceId, setDeviceId] = useState('all')
  const [view, setView] = useState<'shift' | 'day' | 'line'>('shift')
  const [from, setFrom] = useState(() => isoDate(new Date()))
  const [to, setTo] = useState(() => isoDate(new Date()))
  const [report, setReport] = useState<UtilizationResponse | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    void studioApi<DeviceDocument[]>('/api/v1/config/devices')
      .then(setDevices)
      .catch((err: unknown) => setError(describeError(err)))
  }, [])

  async function load(nextFrom = from, nextTo = to, nextDevice = deviceId) {
    const start = new Date(`${nextFrom}T00:00:00`).getTime()
    const end = new Date(`${nextTo}T23:59:59`).getTime()
    const query = new URLSearchParams({ from: String(start), to: String(end) })
    if (nextDevice !== 'all') query.set('deviceId', nextDevice)
    try {
      setReport(await studioApi<UtilizationResponse>(`/api/v1/utilization?${query}`))
      setError('')
    } catch (err) {
      setError(describeError(err))
    }
  }

  useEffect(() => {
    void load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const rows = view === 'day' ? report?.days ?? [] : view === 'line' ? report?.lines ?? [] : report?.shifts ?? []
  const chart = rows
    .filter((row) => view === 'line' || row.deviceId)
    .slice(0, 16)
    .map((row) => ({
      name: view === 'line' ? `${row.line} ${row.shift}` : row.displayName || row.deviceId,
      utilization: Math.round(row.utilization * 1000) / 10,
      parts: Math.round(row.partCount),
    }))

  function exportQuery() {
    const start = new Date(`${from}T00:00:00`).getTime()
    const end = new Date(`${to}T23:59:59`).getTime()
    const query = new URLSearchParams({ from: String(start), to: String(end), view })
    if (deviceId !== 'all') query.set('deviceId', deviceId)
    return query
  }

  return (
    <PageShell
      title='稼动率 / 产量'
      description='运行时间除以班次计划时间。产量按计数差值计算，计数回零时把新读数当作复位后的产量。'
      actions={
        <div className='flex gap-2'>
          <Button
            variant='outline'
            onClick={() => void downloadFile(`/api/v1/utilization.csv?${exportQuery()}`, 'utilization.csv').catch((err: unknown) => setError(describeError(err)))}
          >
            CSV
          </Button>
          <Button
            variant='outline'
            onClick={() => void downloadFile(`/api/v1/utilization.xls?${exportQuery()}`, 'utilization.xls').catch((err: unknown) => setError(describeError(err)))}
          >
            Excel
          </Button>
        </div>
      }
    >
      {error ? <p className='mb-3 text-sm text-destructive'>{error}</p> : null}
      <div className='mb-4 flex flex-wrap items-end gap-3'>
        <label className='grid gap-1 text-xs text-muted-foreground'>
          从
          <Input type='date' value={from} onChange={(event) => setFrom(event.target.value)} />
        </label>
        <label className='grid gap-1 text-xs text-muted-foreground'>
          到
          <Input type='date' value={to} onChange={(event) => setTo(event.target.value)} />
        </label>
        <Select value={deviceId} onValueChange={setDeviceId}>
          <SelectTrigger className='w-48'>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value='all'>全部设备</SelectItem>
            {devices.map((device) => (
              <SelectItem key={device.metadata.id} value={device.metadata.id}>
                {device.metadata.displayName || device.metadata.id}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Select value={view} onValueChange={(value) => setView(value as typeof view)}>
          <SelectTrigger className='w-36'>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value='shift'>按班次</SelectItem>
            <SelectItem value='day'>按日</SelectItem>
            <SelectItem value='line'>按产线</SelectItem>
          </SelectContent>
        </Select>
        <Button onClick={() => void load()}>查询</Button>
      </div>
      <Card className='mb-4'>
        <CardHeader>
          <CardTitle>稼动率</CardTitle>
        </CardHeader>
        <CardContent className='h-64'>
          {chart.length === 0 ? (
            <p className='text-sm text-muted-foreground'>这个范围还没有班次数据。</p>
          ) : (
            <ResponsiveContainer width='100%' height='100%'>
              <BarChart data={chart} margin={{ top: 8, right: 8, left: 0, bottom: 8 }}>
                <CartesianGrid strokeDasharray='3 3' className='stroke-border' />
                <XAxis dataKey='name' tick={{ fontSize: 11 }} interval={0} />
                <YAxis unit='%' width={40} tick={{ fontSize: 11 }} />
                <Tooltip />
                <Bar dataKey='utilization' name='稼动率 %' fill='var(--chart-2)' radius={4} />
              </BarChart>
            </ResponsiveContainer>
          )}
        </CardContent>
      </Card>
      <Card>
        <CardContent className='pt-6'>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>日期</TableHead>
                <TableHead>班次</TableHead>
                <TableHead>车间 / 产线</TableHead>
                <TableHead>设备</TableHead>
                <TableHead>运行</TableHead>
                <TableHead>待机</TableHead>
                <TableHead>报警</TableHead>
                <TableHead>离线</TableHead>
                <TableHead>停机</TableHead>
                <TableHead>计划</TableHead>
                <TableHead>稼动率</TableHead>
                <TableHead>产量</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.map((row, index) => (
                <ReportRow key={`${row.day}-${row.shift}-${row.deviceId}-${row.line}-${index}`} row={row} />
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
    </PageShell>
  )
}

function ReportRow({ row }: { row: UtilizationRow }) {
  return (
    <TableRow>
      <TableCell>{row.day}</TableCell>
      <TableCell>{row.shift || '全天'}</TableCell>
      <TableCell>
        {row.workshop} / {row.line}
      </TableCell>
      <TableCell>{row.displayName || row.deviceId || '—'}</TableCell>
      <TableCell>{formatMinutes(row.runMs)}</TableCell>
      <TableCell>{formatMinutes(row.idleMs)}</TableCell>
      <TableCell>{formatMinutes(row.alarmMs)}</TableCell>
      <TableCell>{formatMinutes(row.offlineMs)}</TableCell>
      <TableCell>{formatMinutes(row.stoppedMs)}</TableCell>
      <TableCell>{formatMinutes(row.plannedMs)}</TableCell>
      <TableCell>{formatPercent(row.utilization)}</TableCell>
      <TableCell>{row.partCount.toFixed(0)}</TableCell>
    </TableRow>
  )
}

function isoDate(date: Date) {
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${date.getFullYear()}-${month}-${day}`
}
