import { useEffect, useState } from 'react'
import { Badge } from '@/components/ui/badge'
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { studioApi, type RuntimeStatus } from '@/lib/studio-api'
import { PageShell } from './page-shell'

type Observation = {
  deviceId: string
  point: string
  value?: string | null
  quality: string
  timestamp: string
}

export function RuntimePage() {
  const [status, setStatus] = useState<RuntimeStatus | null>(null)
  const [observations, setObservations] = useState<Observation[]>([])
  const [lines, setLines] = useState<string[]>([])
  const [error, setError] = useState('')

  useEffect(() => {
    let stop = false
    async function load() {
      try {
        const [nextStatus, samples, logs] = await Promise.all([
          studioApi<RuntimeStatus>('/api/v1/runtime/status'),
          studioApi<{ observations: Observation[] }>('/api/v1/runtime/observations?limit=40'),
          studioApi<{ lines: string[] }>('/api/v1/runtime/logs/tail?lines=40'),
        ])
        if (stop) return
        setStatus(nextStatus)
        setObservations(samples.observations)
        setLines(logs.lines)
        setError('')
      } catch (err) {
        if (!stop) setError(err instanceof Error ? err.message : '加载失败')
      }
    }
    void load()
    const timer = window.setInterval(() => void load(), 3000)
    return () => {
      stop = true
      window.clearInterval(timer)
    }
  }, [])

  const live = status?.mode === 'live'

  return (
    <PageShell
      title='运行态'
      description={
        live
          ? '数据来自本进程的采集会话。发布后会立刻重新加载已发布配置。'
          : '采集未启动，当前是模拟数据。用默认方式启动 Host 后会切换为真实状态。'
      }
    >
      {error ? <p className='mb-3 text-sm text-destructive'>{error}</p> : null}
      <div className='mb-4'>
        <Badge variant={live ? 'default' : 'secondary'}>
          {live ? `live · ${(status?.activeRevision || '').slice(0, 12)}` : 'mock'}
        </Badge>
      </div>
      <div className='grid gap-4 xl:grid-cols-2'>
        <Card>
          <CardHeader>
            <CardTitle>设备</CardTitle>
          </CardHeader>
          <CardContent>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>设备</TableHead>
                  <TableHead>适配器</TableHead>
                  <TableHead>状态</TableHead>
                  <TableHead>说明</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {status?.devices.map((device) => (
                  <TableRow key={device.id}>
                    <TableCell>
                      <div>{device.displayName}</div>
                      <div className='text-xs text-muted-foreground'>{device.id}</div>
                    </TableCell>
                    <TableCell>{device.adapter}</TableCell>
                    <TableCell>{statusLabel(device.status)}</TableCell>
                    <TableCell className='text-xs'>{device.message}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>最近观测</CardTitle>
          </CardHeader>
          <CardContent className='max-h-80 overflow-auto'>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>设备</TableHead>
                  <TableHead>点位</TableHead>
                  <TableHead>值</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {observations.map((item, index) => (
                  <TableRow key={`${item.deviceId}-${item.point}-${item.timestamp}-${index}`}>
                    <TableCell>{item.deviceId}</TableCell>
                    <TableCell>{item.point}</TableCell>
                    <TableCell>
                      {item.value} <span className='text-xs text-muted-foreground'>{item.quality}</span>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      </div>
      <Card className='mt-4'>
        <CardHeader>
          <CardTitle>日志</CardTitle>
        </CardHeader>
        <CardContent>
          <pre className='max-h-64 overflow-auto text-xs'>{lines.join('\n') || '暂无日志'}</pre>
        </CardContent>
      </Card>
    </PageShell>
  )
}

function statusLabel(status: string) {
  if (status === 'online') return '在线'
  if (status === 'offline') return '离线'
  if (status === 'disabled') return '禁用'
  if (status === 'degraded') return '降级'
  return status
}
