import { useEffect, useState } from 'react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
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
import {
  brandOfAdapter,
  describeError,
  studioApi,
  type AlarmRow,
  type CatalogOverview,
  type DeviceDocument,
  type SampleRow,
} from '@/lib/studio-api'
import { cn } from '@/lib/utils'
import { PageShell } from './page-shell'

export function LivePage({ initialDeviceId = '' }: { initialDeviceId?: string }) {
  const [devices, setDevices] = useState<DeviceDocument[]>([])
  const [catalog, setCatalog] = useState<CatalogOverview | null>(null)
  const [deviceId, setDeviceId] = useState(initialDeviceId)
  const [samples, setSamples] = useState<SampleRow[]>([])
  const [history, setHistory] = useState<SampleRow[]>([])
  const [alarms, setAlarms] = useState<AlarmRow[]>([])
  const [pointId, setPointId] = useState('all')
  const [message, setMessage] = useState('')

  useEffect(() => {
    void Promise.all([
      studioApi<DeviceDocument[]>('/api/v1/config/devices'),
      studioApi<CatalogOverview>('/api/v1/catalog/brands'),
    ])
      .then(([deviceItems, overview]) => {
        setDevices(deviceItems)
        setCatalog(overview)
        setDeviceId((current) => current || deviceItems[0]?.metadata.id || '')
      })
      .catch((error: unknown) => setMessage(describeError(error)))
  }, [])

  useEffect(() => {
    if (!deviceId) return
    let stop = false
    async function load() {
      try {
        const [latest, alarmBody] = await Promise.all([
          studioApi<{ samples: SampleRow[] }>(`/api/v1/devices/${encodeURIComponent(deviceId)}/latest`),
          studioApi<{ alarms: AlarmRow[] }>(`/api/v1/alarms?deviceId=${encodeURIComponent(deviceId)}&limit=20`),
        ])
        if (stop) return
        setSamples(latest.samples)
        setAlarms(alarmBody.alarms)
        setMessage(latest.samples.length === 0 ? '还没有采样。发布并等待一个扫描周期后再看。' : '')
      } catch (error) {
        if (!stop) setMessage(describeError(error))
      }
    }
    void load()
    const timer = window.setInterval(() => void load(), 2000)
    return () => {
      stop = true
      window.clearInterval(timer)
    }
  }, [deviceId])

  useEffect(() => {
    if (!deviceId) return
    const from = Date.now() - 60 * 60 * 1000
    const itemQuery = pointId === 'all' ? '' : `&items=${encodeURIComponent(pointId)}`
    void studioApi<{ samples: SampleRow[] }>(
      `/api/v1/samples/history?deviceId=${encodeURIComponent(deviceId)}&from=${from}&bucketMs=5000${itemQuery}`
    )
      .then((body) => setHistory(body.samples))
      .catch((error: unknown) => setMessage(describeError(error)))
  }, [deviceId, pointId, samples.length])

  const device = devices.find((item) => item.metadata.id === deviceId)
  const brand =
    catalog?.brands.find((item) => item.id === device?.spec.brandId) ??
    brandOfAdapter(catalog, device?.spec.adapter)
  const names = new Map((brand?.items ?? catalog?.items ?? []).map((item) => [item.id, item.nameZh]))

  return (
    <PageShell
      title='实时值'
      description='采集写入数据库的最新值和最近一小时历史。模拟器只产生该品牌目录里有的数据项。MQTT 仍按原主题发布。'
    >
      <div className='grid items-start gap-3 lg:grid-cols-[16rem_minmax(0,1fr)]'>
        <Card className='gap-0 overflow-hidden py-0'>
          <div className='border-b px-3 py-2 text-sm font-medium'>设备</div>
          <div className='grid gap-0.5 p-1.5'>
            {devices.length === 0 ? (
              <p className='px-2 py-3 text-sm text-muted-foreground'>还没有设备。</p>
            ) : null}
            {devices.map((item) => (
              <button
                key={item.metadata.id}
                type='button'
                className={cn(
                  'rounded-md px-2 py-2 text-start text-sm hover:bg-accent',
                  item.metadata.id === deviceId && 'bg-accent font-medium'
                )}
                onClick={() => setDeviceId(item.metadata.id)}
              >
                <span className='block truncate'>{item.metadata.displayName || item.metadata.id}</span>
                <span className='block truncate font-mono text-xs text-muted-foreground'>{item.spec.adapter}</span>
              </button>
            ))}
          </div>
        </Card>
        <div className='grid min-w-0 gap-3'>
          {message ? <p className='text-sm text-muted-foreground'>{message}</p> : null}
          <Card>
            <CardHeader>
              <CardTitle>
                最新值
                {brand ? <span className='ms-2 text-sm font-normal text-muted-foreground'>{brand.nameZh}</span> : null}
              </CardTitle>
            </CardHeader>
            <CardContent>
              <SampleTable rows={samples} names={names} />
            </CardContent>
          </Card>
          <Card>
            <CardHeader className='flex flex-row items-center justify-between gap-3'>
              <CardTitle>历史（最近 1 小时，5 秒抽样）</CardTitle>
              <Select value={pointId} onValueChange={setPointId}>
                <SelectTrigger className='w-56'>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value='all'>全部数据项</SelectItem>
                  {samples.map((sample) => (
                    <SelectItem key={sample.pointId} value={sample.pointId}>
                      {names.get(sample.pointId) || sample.pointId}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </CardHeader>
            <CardContent>
              <SampleTable rows={history.slice(-80)} names={names} />
            </CardContent>
          </Card>
          <Card>
            <CardHeader className='flex flex-row items-center justify-between'>
              <CardTitle>报警</CardTitle>
              <Button variant='outline' size='sm' onClick={() => setDeviceId(deviceId)}>
                刷新
              </Button>
            </CardHeader>
            <CardContent>
              {alarms.length === 0 ? (
                <p className='text-sm text-muted-foreground'>这台设备还没有报警记录。</p>
              ) : (
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>时间</TableHead>
                      <TableHead>点位</TableHead>
                      <TableHead>内容</TableHead>
                      <TableHead>状态</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {alarms.map((alarm) => (
                      <TableRow key={alarm.id}>
                        <TableCell className='text-xs'>{formatTime(alarm.raisedUnixMs)}</TableCell>
                        <TableCell>{names.get(alarm.pointId) || alarm.pointId}</TableCell>
                        <TableCell>{alarm.message}</TableCell>
                        <TableCell>
                          <Badge variant={alarm.active ? 'default' : 'secondary'}>
                            {alarm.active ? '活动' : '已恢复'}
                          </Badge>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}
            </CardContent>
          </Card>
        </div>
      </div>
    </PageShell>
  )
}

function SampleTable({ rows, names }: { rows: SampleRow[]; names: Map<string, string> }) {
  return (
    <div className='overflow-auto'>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>数据项</TableHead>
            <TableHead>值</TableHead>
            <TableHead>质量</TableHead>
            <TableHead>时间</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.length === 0 ? (
            <TableRow>
              <TableCell colSpan={4} className='text-muted-foreground'>
                暂无数据。
              </TableCell>
            </TableRow>
          ) : null}
          {rows.map((row) => (
            <TableRow key={`${row.pointId}-${row.timestampUnixMs}`}>
              <TableCell>
                <div>{names.get(row.pointId) || row.pointId}</div>
                <div className='font-mono text-xs text-muted-foreground'>{row.pointId}</div>
              </TableCell>
              <TableCell>
                {row.value ?? '—'}
                {row.unit ? <span className='ms-1 text-xs text-muted-foreground'>{row.unit}</span> : null}
              </TableCell>
              <TableCell>{row.quality}</TableCell>
              <TableCell className='text-xs'>{formatTime(row.timestampUnixMs)}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}

function formatTime(unixMs: number) {
  if (!unixMs) return '—'
  return new Date(unixMs).toLocaleString('zh-CN', { hour12: false })
}
