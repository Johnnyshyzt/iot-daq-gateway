import { useEffect, useMemo, useState } from 'react'
import {
  CartesianGrid,
  Legend,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
  Brush,
} from 'recharts'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { describeError, studioApi, type CatalogOverview, type DeviceDocument } from '@/lib/studio-api'
import { PageShell } from '@/features/daq/page-shell'
import { downloadFile, formatClock } from './format'
import type { SeriesResponse } from './types'

const palette = ['#0f766e', '#b45309', '#1d4ed8', '#be123c', '#6d28d9', '#0369a1', '#3f6212', '#9a3412']
const ranges = [
  { id: '1h', label: '1 小时', ms: 60 * 60 * 1000 },
  { id: '8h', label: '8 小时', ms: 8 * 60 * 60 * 1000 },
  { id: '24h', label: '24 小时', ms: 24 * 60 * 60 * 1000 },
  { id: '7d', label: '7 天', ms: 7 * 24 * 60 * 60 * 1000 },
]

const defaultPoints = ['spindleSpeed', 'spindleLoad', 'feedRate', 'partCount']

export function HistoryPage({ initialDeviceId = '' }: { initialDeviceId?: string }) {
  const [devices, setDevices] = useState<DeviceDocument[]>([])
  const [names, setNames] = useState<Map<string, string>>(new Map())
  const [pickedDevices, setPickedDevices] = useState<string[]>([])
  const [pickedPoints, setPickedPoints] = useState<string[]>(defaultPoints)
  const [range, setRange] = useState('8h')
  const [customFrom, setCustomFrom] = useState('')
  const [customTo, setCustomTo] = useState('')
  const [series, setSeries] = useState<SeriesResponse | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  useEffect(() => {
    void Promise.all([
      studioApi<DeviceDocument[]>('/api/v1/config/devices'),
      studioApi<CatalogOverview>('/api/v1/catalog/brands'),
    ])
      .then(([deviceItems, catalog]) => {
        setDevices(deviceItems)
        setNames(new Map(catalog.items.map((item) => [item.id, item.nameZh])))
        setPickedDevices((current) => {
          if (current.length > 0) return current
          if (initialDeviceId && deviceItems.some((item) => item.metadata.id === initialDeviceId)) return [initialDeviceId]
          return deviceItems[0] ? [deviceItems[0].metadata.id] : []
        })
      })
      .catch((err: unknown) => setError(describeError(err)))
  }, [initialDeviceId])

  const pointChoices = useMemo(() => {
    const ids = new Set<string>(defaultPoints)
    for (const point of series?.series ?? []) ids.add(point.pointId)
    return [...ids]
  }, [series])

  async function load() {
    if (pickedDevices.length === 0 || pickedPoints.length === 0) {
      setError('请至少选择一台设备和一个数据项')
      return
    }
    setLoading(true)
    try {
      const now = Date.now()
      const preset = ranges.find((item) => item.id === range)
      const from = range === 'custom' && customFrom ? new Date(customFrom).getTime() : now - (preset?.ms ?? ranges[1].ms)
      const to = range === 'custom' && customTo ? new Date(customTo).getTime() : now
      const query = new URLSearchParams({
        devices: pickedDevices.join(','),
        points: pickedPoints.join(','),
        from: String(from),
        to: String(to),
      })
      const next = await studioApi<SeriesResponse>(`/api/v1/samples/series?${query}`)
      setSeries(next)
      setError(next.series.length === 0 ? '这个范围里没有数值历史。' : '')
    } catch (err) {
      setError(describeError(err))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    if (pickedDevices.length === 0) return
    void load()
    // The button and range changes call load explicitly; the first device list should query once.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pickedDevices.join('|')])

  const chart = useMemo(() => pivot(series), [series])

  return (
    <PageShell
      title='历史曲线'
      description='按设备和数据项查看聚合后的曲线。长时间范围由服务端按时间桶做平均、最小和最大，页面不拉取原始采样。'
      actions={
        <Button
          variant='outline'
          disabled={!series || series.series.length === 0}
          onClick={() => {
            if (!series) return
            const query = new URLSearchParams({
              devices: pickedDevices.join(','),
              points: pickedPoints.join(','),
              from: String(series.fromUnixMs),
              to: String(series.toUnixMs),
              bucketMs: String(series.bucketMs),
            })
            void downloadFile(`/api/v1/samples/series.csv?${query}`, 'history.csv').catch((err: unknown) =>
              setError(describeError(err))
            )
          }}
        >
          导出 CSV
        </Button>
      }
    >
      <div className='grid gap-4 lg:grid-cols-[18rem_minmax(0,1fr)]'>
        <Card>
          <CardHeader>
            <CardTitle>条件</CardTitle>
          </CardHeader>
          <CardContent className='grid gap-3 text-sm'>
            <div>
              <div className='mb-1 font-medium'>设备</div>
              <div className='grid max-h-40 gap-1 overflow-auto'>
                {devices.map((device) => (
                  <label key={device.metadata.id} className='flex items-center gap-2'>
                    <Checkbox
                      checked={pickedDevices.includes(device.metadata.id)}
                      onCheckedChange={(checked) =>
                        setPickedDevices((current) =>
                          checked
                            ? [...current, device.metadata.id]
                            : current.filter((id) => id !== device.metadata.id)
                        )
                      }
                    />
                    <span className='truncate'>{device.metadata.displayName || device.metadata.id}</span>
                  </label>
                ))}
              </div>
            </div>
            <div>
              <div className='mb-1 font-medium'>数据项</div>
              <div className='grid gap-1'>
                {pointChoices.map((point) => (
                  <label key={point} className='flex items-center gap-2'>
                    <Checkbox
                      checked={pickedPoints.includes(point)}
                      onCheckedChange={(checked) =>
                        setPickedPoints((current) =>
                          checked ? [...current, point] : current.filter((id) => id !== point)
                        )
                      }
                    />
                    <span>{names.get(point) || point}</span>
                  </label>
                ))}
              </div>
            </div>
            <div className='flex flex-wrap gap-1'>
              {ranges.map((item) => (
                <Button key={item.id} size='sm' variant={range === item.id ? 'default' : 'outline'} onClick={() => setRange(item.id)}>
                  {item.label}
                </Button>
              ))}
              <Button size='sm' variant={range === 'custom' ? 'default' : 'outline'} onClick={() => setRange('custom')}>
                自定义
              </Button>
            </div>
            {range === 'custom' ? (
              <div className='grid gap-2'>
                <input className='rounded-md border bg-transparent px-2 py-1' type='datetime-local' value={customFrom} onChange={(event) => setCustomFrom(event.target.value)} />
                <input className='rounded-md border bg-transparent px-2 py-1' type='datetime-local' value={customTo} onChange={(event) => setCustomTo(event.target.value)} />
              </div>
            ) : null}
            <Button onClick={() => void load()} disabled={loading}>
              {loading ? '查询中…' : '查询'}
            </Button>
            {series ? <p className='text-xs text-muted-foreground'>聚合桶 {Math.round(series.bucketMs / 1000)} 秒</p> : null}
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>曲线</CardTitle>
          </CardHeader>
          <CardContent>
            {error ? <p className='mb-3 text-sm text-muted-foreground'>{error}</p> : null}
            {chart.rows.length === 0 ? (
              <p className='text-sm text-muted-foreground'>选择条件后查询。拖动下方滑块可以放大一段时间。</p>
            ) : (
              <div className='h-96'>
                <ResponsiveContainer width='100%' height='100%'>
                  <LineChart data={chart.rows} margin={{ top: 8, right: 12, left: 0, bottom: 8 }}>
                    <CartesianGrid strokeDasharray='3 3' className='stroke-border' />
                    <XAxis dataKey='label' minTickGap={24} tick={{ fontSize: 11 }} />
                    <YAxis tick={{ fontSize: 11 }} width={48} />
                    <Tooltip />
                    <Legend />
                    <Brush dataKey='label' height={24} travellerWidth={8} />
                    {chart.keys.map((key, index) => (
                      <Line
                        key={key}
                        type='monotone'
                        dataKey={key}
                        name={chart.labels.get(key) || key}
                        stroke={palette[index % palette.length]}
                        dot={false}
                        strokeWidth={1.6}
                        connectNulls
                      />
                    ))}
                  </LineChart>
                </ResponsiveContainer>
              </div>
            )}
          </CardContent>
        </Card>
      </div>
    </PageShell>
  )
}

function pivot(series: SeriesResponse | null) {
  const rows = new Map<number, Record<string, number | string>>()
  const labels = new Map<string, string>()
  const keys: string[] = []
  for (const point of series?.series ?? []) {
    const key = `${point.deviceId}·${point.pointId}`
    if (!labels.has(key)) {
      labels.set(key, key)
      keys.push(key)
    }
    const row = rows.get(point.timestampUnixMs) ?? { t: point.timestampUnixMs, label: formatClock(point.timestampUnixMs) }
    row[key] = Math.round(point.avg * 1000) / 1000
    rows.set(point.timestampUnixMs, row)
  }
  return { rows: [...rows.values()].sort((a, b) => Number(a.t) - Number(b.t)), keys, labels }
}
