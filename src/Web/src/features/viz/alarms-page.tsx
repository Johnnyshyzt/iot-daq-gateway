import { useEffect, useState } from 'react'
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { Badge } from '@/components/ui/badge'
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
import { canWrite, describeError, studioApi, type DeviceDocument } from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'
import { downloadFile, formatMinutes, formatTime } from './format'
import type { AlarmItem, AlarmStat } from './types'

export function AlarmsPage() {
  const writable = canWrite(useAuthStore((state) => state.auth.user?.role[0]))
  const [devices, setDevices] = useState<DeviceDocument[]>([])
  const [deviceId, setDeviceId] = useState('all')
  const [scope, setScope] = useState('active')
  const [code, setCode] = useState('')
  const [alarms, setAlarms] = useState<AlarmItem[]>([])
  const [byCode, setByCode] = useState<AlarmStat[]>([])
  const [byDevice, setByDevice] = useState<AlarmStat[]>([])
  const [error, setError] = useState('')

  useEffect(() => {
    void studioApi<DeviceDocument[]>('/api/v1/config/devices')
      .then(setDevices)
      .catch((err: unknown) => setError(describeError(err)))
  }, [])

  async function load() {
    const query = new URLSearchParams({ limit: '200' })
    if (deviceId !== 'all') query.set('deviceId', deviceId)
    if (scope === 'active') query.set('active', 'true')
    if (scope === 'history') query.set('active', 'false')
    if (code.trim()) query.set('code', code.trim())
    const from = Date.now() - 7 * 24 * 60 * 60 * 1000
    query.set('from', String(from))
    try {
      const [list, stats] = await Promise.all([
        studioApi<{ alarms: AlarmItem[] }>(`/api/v1/alarms?${query}`),
        studioApi<{ byDevice: AlarmStat[]; byCode: AlarmStat[] }>(
          `/api/v1/alarms/stats?top=8&from=${from}${deviceId === 'all' ? '' : `&deviceId=${encodeURIComponent(deviceId)}`}`
        ),
      ])
      setAlarms(list.alarms)
      setByCode(stats.byCode)
      setByDevice(stats.byDevice)
      setError('')
    } catch (err) {
      setError(describeError(err))
    }
  }

  useEffect(() => {
    void load()
    const timer = window.setInterval(() => void load(), 4000)
    return () => window.clearInterval(timer)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [deviceId, scope, code])

  async function acknowledge(id: string) {
    await studioApi(`/api/v1/alarms/${encodeURIComponent(id)}/ack`, { method: 'POST' })
    await load()
  }

  return (
    <PageShell
      title='报警'
      description='由目录报警项和状态变为报警推导。活动报警可以确认，历史保留开始、结束和持续时长。'
      actions={
        <Button
          variant='outline'
          onClick={() => {
            const query = new URLSearchParams()
            if (deviceId !== 'all') query.set('deviceId', deviceId)
            const from = Date.now() - 7 * 24 * 60 * 60 * 1000
            query.set('from', String(from))
            void downloadFile(`/api/v1/alarms.csv?${query}`, 'alarms.csv').catch((err: unknown) => setError(describeError(err)))
          }}
        >
          导出 CSV
        </Button>
      }
    >
      {error ? <p className='mb-3 text-sm text-destructive'>{error}</p> : null}
      <div className='mb-4 flex flex-wrap items-end gap-3'>
        <Select value={deviceId} onValueChange={setDeviceId}>
          <SelectTrigger className='w-52'>
            <SelectValue placeholder='设备' />
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
        <Select value={scope} onValueChange={setScope}>
          <SelectTrigger className='w-36'>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value='active'>活动</SelectItem>
            <SelectItem value='history'>已恢复</SelectItem>
            <SelectItem value='all'>全部</SelectItem>
          </SelectContent>
        </Select>
        <Input className='w-40' placeholder='报警代码' value={code} onChange={(event) => setCode(event.target.value)} />
        <Button variant='outline' onClick={() => void load()}>
          刷新
        </Button>
      </div>
      <div className='mb-4 grid gap-4 lg:grid-cols-2'>
        <Card>
          <CardHeader>
            <CardTitle>代码 Top</CardTitle>
          </CardHeader>
          <CardContent className='h-56'>
            <StatChart rows={byCode} />
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>设备 Top</CardTitle>
          </CardHeader>
          <CardContent className='h-56'>
            <StatChart rows={byDevice} />
          </CardContent>
        </Card>
      </div>
      <Card>
        <CardContent className='pt-6'>
          {alarms.length === 0 ? (
            <p className='text-sm text-muted-foreground'>没有符合条件的报警。</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>开始</TableHead>
                  <TableHead>设备</TableHead>
                  <TableHead>代码</TableHead>
                  <TableHead>内容</TableHead>
                  <TableHead>持续(分)</TableHead>
                  <TableHead>状态</TableHead>
                  <TableHead />
                </TableRow>
              </TableHeader>
              <TableBody>
                {alarms.map((alarm) => (
                  <TableRow key={alarm.id}>
                    <TableCell className='text-xs'>{formatTime(alarm.raisedUnixMs)}</TableCell>
                    <TableCell>{alarm.deviceId}</TableCell>
                    <TableCell className='font-mono text-xs'>{alarm.code || '—'}</TableCell>
                    <TableCell>{alarm.message}</TableCell>
                    <TableCell>{formatMinutes(alarm.durationMs ?? 0)}</TableCell>
                    <TableCell>
                      <Badge variant={alarm.active ? 'destructive' : 'secondary'}>{alarm.active ? '活动' : '已恢复'}</Badge>
                      {alarm.acknowledged ? <span className='ms-2 text-xs text-muted-foreground'>已确认</span> : null}
                    </TableCell>
                    <TableCell>
                      <Button
                        size='sm'
                        variant='outline'
                        disabled={!writable || alarm.acknowledged}
                        onClick={() => void acknowledge(alarm.id).catch((err: unknown) => setError(describeError(err)))}
                      >
                        确认
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
    </PageShell>
  )
}

function StatChart({ rows }: { rows: AlarmStat[] }) {
  if (rows.length === 0) return <p className='text-sm text-muted-foreground'>暂无统计。</p>
  const data = rows.map((row) => ({ name: row.key || row.label, count: row.count }))
  return (
    <ResponsiveContainer width='100%' height='100%'>
      <BarChart data={data} margin={{ top: 8, right: 8, left: 0, bottom: 8 }}>
        <CartesianGrid strokeDasharray='3 3' className='stroke-border' />
        <XAxis dataKey='name' tick={{ fontSize: 11 }} interval={0} />
        <YAxis allowDecimals={false} width={32} tick={{ fontSize: 11 }} />
        <Tooltip />
        <Bar dataKey='count' name='次数' fill='var(--chart-1)' radius={4} />
      </BarChart>
    </ResponsiveContainer>
  )
}
