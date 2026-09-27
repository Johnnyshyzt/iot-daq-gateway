import { useEffect, useState } from 'react'
import { Bar, BarChart, CartesianGrid, ComposedChart, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
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
import { downloadFile, formatMinutes, formatPercent } from '@/features/viz/format'
import { canOperate, describeError, studioApi, type DeviceDocument } from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'

type OeeRow = {
  deviceId: string
  displayName: string
  workshop: string
  line: string
  day: string
  shift: string
  plannedMs: number
  runMs: number
  totalParts: number
  goodParts: number
  scrapParts: number
  idealCycleSeconds: number | null
  availability: number
  performance: number | null
  quality: number
  oee: number | null
  flag: string
}

type WaterfallStep = { name: string; offsetMs: number; valueMs: number; kind: string }
type ParetoBar = { reason: string; durationMs: number; share: number; cumulative: number }

type OeeReport = {
  rows: OeeRow[]
  days: OeeRow[]
  lines: OeeRow[]
  pareto: ParetoBar[]
  waterfall: WaterfallStep[]
  formula: string
}

const flagText: Record<string, string> = {
  noPlannedTime: '计划生产时间为 0',
  missingIdealCycle: '缺少理想节拍',
  noRunTime: '没有运行时间',
}

function isoDate(date = new Date()) {
  const pad = (value: number) => String(value).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

function percentOrDash(value: number | null | undefined) {
  if (value === null || value === undefined) return '—'
  return formatPercent(value)
}

export function OeePage() {
  const operable = canOperate(useAuthStore((state) => state.auth.user?.role[0]))
  const [devices, setDevices] = useState<DeviceDocument[]>([])
  const [deviceId, setDeviceId] = useState('all')
  const [view, setView] = useState<'shift' | 'day' | 'line'>('shift')
  const [from, setFrom] = useState(() => isoDate(new Date(Date.now() - 86400000)))
  const [to, setTo] = useState(() => isoDate())
  const [licensed, setLicensed] = useState(true)
  const [message, setMessage] = useState('')
  const [formula, setFormula] = useState('')
  const [report, setReport] = useState<OeeReport | null>(null)
  const [scrapDevice, setScrapDevice] = useState('')
  const [scrapQty, setScrapQty] = useState('1')
  const [error, setError] = useState('')

  useEffect(() => {
    void studioApi<DeviceDocument[]>('/api/v1/config/devices')
      .then((list) => {
        setDevices(list)
        setScrapDevice(list[0]?.metadata.id ?? '')
      })
      .catch((err: unknown) => setError(describeError(err)))
  }, [])

  async function load(nextFrom = from, nextTo = to, nextDevice = deviceId) {
    const start = new Date(`${nextFrom}T00:00:00`).getTime()
    const end = new Date(`${nextTo}T23:59:59`).getTime()
    const query = new URLSearchParams({ from: String(start), to: String(end) })
    if (nextDevice !== 'all') query.set('deviceId', nextDevice)
    try {
      const body = await studioApi<{ licensed: boolean; message?: string; formula?: string; report?: OeeReport }>(`/api/v1/oee?${query}`)
      setLicensed(body.licensed)
      setMessage(body.message ?? '')
      setFormula(body.report?.formula || body.formula || '')
      setReport(body.report ?? null)
      setError('')
    } catch (err) {
      setError(describeError(err))
    }
  }

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const rows = view === 'day' ? report?.days ?? [] : view === 'line' ? report?.lines ?? [] : report?.rows ?? []
  const scored = (report?.rows ?? []).filter((row) => row.oee !== null)
  const average = scored.length ? scored.reduce((sum, row) => sum + (row.oee ?? 0), 0) / scored.length : null
  const availability = (report?.rows ?? []).length
    ? (report?.rows ?? []).reduce((sum, row) => sum + row.availability, 0) / (report?.rows.length ?? 1)
    : null
  const waterfall = (report?.waterfall ?? []).map((step) => ({
    name: step.name,
    base: Math.round(step.offsetMs / 60000),
    minutes: Math.round(step.valueMs / 60000),
    fill: step.kind === 'result' ? 'var(--chart-2)' : step.kind === 'total' ? 'var(--chart-1)' : 'var(--chart-4)',
  }))
  const pareto = (report?.pareto ?? []).map((bar) => ({
    name: bar.reason,
    minutes: Math.round(bar.durationMs / 60000),
    cumulative: Math.round(bar.cumulative * 1000) / 10,
  }))
  const trend = (report?.days ?? []).map((row) => ({
    name: `${row.day} ${row.displayName || row.line}`,
    oee: row.oee === null ? null : Math.round(row.oee * 1000) / 10,
  }))

  function exportQuery() {
    const start = new Date(`${from}T00:00:00`).getTime()
    const end = new Date(`${to}T23:59:59`).getTime()
    const query = new URLSearchParams({ from: String(start), to: String(end), view })
    if (deviceId !== 'all') query.set('deviceId', deviceId)
    return query
  }

  async function addScrap() {
    try {
      await studioApi('/api/v1/oee/scrap', {
        method: 'POST',
        body: JSON.stringify({ deviceId: scrapDevice, quantity: Number(scrapQty) }),
      })
      setError('')
      await load()
    } catch (err) {
      setError(describeError(err))
    }
  }

  return (
    <PageShell
      title='OEE'
      description='OEE = 可用率 × 性能率 × 质量率。'
      actions={
        <div className='flex gap-2'>
          <Button variant='outline' onClick={() => void downloadFile(`/api/v1/oee.csv?${exportQuery()}`, 'oee.csv').catch((err: unknown) => setError(describeError(err)))}>CSV</Button>
          <Button variant='outline' onClick={() => void downloadFile(`/api/v1/oee.xls?${exportQuery()}`, 'oee.xls').catch((err: unknown) => setError(describeError(err)))}>Excel</Button>
        </div>
      }
    >
      {error ? <p className='mb-3 text-sm text-destructive'>{error}</p> : null}
      {!licensed ? <p className='mb-3 text-sm text-destructive'>{message || '当前授权未包含 OEE。'}</p> : null}
      {formula ? <p className='mb-4 text-sm text-muted-foreground'>{formula}</p> : null}
      <div className='mb-4 flex flex-wrap items-end gap-3'>
        <label className='grid gap-1 text-xs text-muted-foreground'>从<Input type='date' value={from} onChange={(event) => setFrom(event.target.value)} /></label>
        <label className='grid gap-1 text-xs text-muted-foreground'>到<Input type='date' value={to} onChange={(event) => setTo(event.target.value)} /></label>
        <Select value={deviceId} onValueChange={setDeviceId}>
          <SelectTrigger className='w-48'><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value='all'>全部设备</SelectItem>
            {devices.map((device) => <SelectItem key={device.metadata.id} value={device.metadata.id}>{device.metadata.displayName || device.metadata.id}</SelectItem>)}
          </SelectContent>
        </Select>
        <Select value={view} onValueChange={(value) => setView(value as 'shift' | 'day' | 'line')}>
          <SelectTrigger className='w-36'><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value='shift'>按班次</SelectItem>
            <SelectItem value='day'>按日</SelectItem>
            <SelectItem value='line'>按产线</SelectItem>
          </SelectContent>
        </Select>
        <Button onClick={() => void load()}>查询</Button>
      </div>
      <div className='mb-4 grid gap-3 sm:grid-cols-4'>
        <Card><CardHeader><CardTitle className='text-sm'>可用率</CardTitle></CardHeader><CardContent className='text-2xl font-semibold'>{percentOrDash(availability)}</CardContent></Card>
        <Card><CardHeader><CardTitle className='text-sm'>性能率</CardTitle></CardHeader><CardContent className='text-2xl font-semibold'>{percentOrDash(report?.rows.find((row) => row.performance !== null)?.performance)}</CardContent></Card>
        <Card><CardHeader><CardTitle className='text-sm'>质量率</CardTitle></CardHeader><CardContent className='text-2xl font-semibold'>{percentOrDash(report?.rows[0]?.quality)}</CardContent></Card>
        <Card><CardHeader><CardTitle className='text-sm'>OEE</CardTitle></CardHeader><CardContent className='text-2xl font-semibold' data-testid='oee-average'>{percentOrDash(average)}</CardContent></Card>
      </div>
      <div className='grid gap-4 lg:grid-cols-2'>
        <Card>
          <CardHeader><CardTitle>损失瀑布</CardTitle></CardHeader>
          <CardContent className='h-72' data-testid='oee-waterfall'>
            <ResponsiveContainer width='100%' height='100%'>
              <BarChart data={waterfall}>
                <CartesianGrid strokeDasharray='3 3' />
                <XAxis dataKey='name' interval={0} angle={-25} textAnchor='end' height={70} tick={{ fontSize: 11 }} />
                <YAxis unit=' 分' />
                <Tooltip />
                <Bar dataKey='base' stackId='loss' fill='transparent' />
                <Bar dataKey='minutes' name='分钟' stackId='loss' fill='var(--chart-4)' radius={4} />
              </BarChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>
        <Card>
          <CardHeader><CardTitle>停机原因帕累托</CardTitle></CardHeader>
          <CardContent className='h-72' data-testid='oee-pareto'>
            <ResponsiveContainer width='100%' height='100%'>
              <ComposedChart data={pareto}>
                <CartesianGrid strokeDasharray='3 3' />
                <XAxis dataKey='name' interval={0} angle={-20} textAnchor='end' height={60} tick={{ fontSize: 11 }} />
                <YAxis yAxisId='min' unit=' 分' />
                <YAxis yAxisId='pct' orientation='right' unit='%' />
                <Tooltip />
                <Bar yAxisId='min' dataKey='minutes' name='分钟' fill='var(--chart-1)' radius={4} />
                <Line yAxisId='pct' dataKey='cumulative' name='累计 %' stroke='var(--chart-5)' />
              </ComposedChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>
      </div>
      <Card className='mt-4'>
        <CardHeader><CardTitle>趋势</CardTitle></CardHeader>
        <CardContent className='h-56'>
          <ResponsiveContainer width='100%' height='100%'>
            <LineChart data={trend}>
              <CartesianGrid strokeDasharray='3 3' />
              <XAxis dataKey='name' tick={{ fontSize: 11 }} />
              <YAxis unit='%' />
              <Tooltip />
              <Line dataKey='oee' name='OEE %' stroke='var(--chart-2)' dot />
            </LineChart>
          </ResponsiveContainer>
        </CardContent>
      </Card>
      <Card className='mt-4'>
        <CardContent className='pt-4'>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>日期</TableHead>
                <TableHead>班次</TableHead>
                <TableHead>设备 / 产线</TableHead>
                <TableHead>计划分</TableHead>
                <TableHead>运行分</TableHead>
                <TableHead>可用率</TableHead>
                <TableHead>性能率</TableHead>
                <TableHead>质量率</TableHead>
                <TableHead>OEE</TableHead>
                <TableHead>产量</TableHead>
                <TableHead>标记</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.length === 0 ? <TableRow><TableCell colSpan={11} className='text-muted-foreground'>这个时间段没有班次数据</TableCell></TableRow> : rows.map((row, index) => (
                <TableRow key={`${row.deviceId}-${row.line}-${row.day}-${row.shift}-${index}`}>
                  <TableCell>{row.day}</TableCell>
                  <TableCell>{row.shift || '全日'}</TableCell>
                  <TableCell>{row.displayName || row.line || row.deviceId}</TableCell>
                  <TableCell>{formatMinutes(row.plannedMs)}</TableCell>
                  <TableCell>{formatMinutes(row.runMs)}</TableCell>
                  <TableCell>{formatPercent(row.availability)}</TableCell>
                  <TableCell>{percentOrDash(row.performance)}</TableCell>
                  <TableCell>{formatPercent(row.quality)}</TableCell>
                  <TableCell>{percentOrDash(row.oee)}</TableCell>
                  <TableCell>{Math.round(row.goodParts)}/{Math.round(row.totalParts)}</TableCell>
                  <TableCell>{flagText[row.flag] || row.flag}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
      <Card className='mt-4'>
        <CardHeader><CardTitle>手工报废</CardTitle></CardHeader>
        <CardContent className='flex flex-wrap items-end gap-3'>
          <label className='grid gap-1 text-xs text-muted-foreground'>
            设备
            <Select value={scrapDevice || 'none'} onValueChange={(value) => setScrapDevice(value === 'none' ? '' : value)} disabled={!operable}>
              <SelectTrigger className='w-48'><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value='none'>选择设备</SelectItem>
                {devices.map((device) => <SelectItem key={device.metadata.id} value={device.metadata.id}>{device.metadata.displayName || device.metadata.id}</SelectItem>)}
              </SelectContent>
            </Select>
          </label>
          <label className='grid gap-1 text-xs text-muted-foreground'>
            数量
            <Input className='w-24' type='number' min={1} value={scrapQty} disabled={!operable} onChange={(event) => setScrapQty(event.target.value)} />
          </label>
          <Button disabled={!operable || !licensed} onClick={() => void addScrap()}>登记报废</Button>
          <p className='text-xs text-muted-foreground'>操作员可以登记报废。质量率也会计入点位 scrapCount 的增量。</p>
        </CardContent>
      </Card>
    </PageShell>
  )
}
