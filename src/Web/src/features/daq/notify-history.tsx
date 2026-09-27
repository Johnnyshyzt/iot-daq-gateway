import { useEffect, useState } from 'react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { canWrite, describeError, studioApi } from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'
import { formatTime } from '@/features/viz/format'
import { PageShell } from './page-shell'

type Delivery = {
  id: number
  channelId: string
  ruleId: string
  kind: string
  deviceId: string
  code: string
  summary: string
  status: string
  attempts: number
  lastError: string
  createdUnixMs: number
  sentUnixMs?: number | null
}

const kindLabels: Record<string, string> = {
  raise: '报警',
  clear: '恢复',
  escalation: '升级',
  test: '测试',
  report: '报表',
}

export function NotifyHistoryPage() {
  const writable = canWrite(useAuthStore((state) => state.auth.user?.role[0]))
  const [rows, setRows] = useState<Delivery[]>([])
  const [error, setError] = useState('')

  async function reload() {
    const body = await studioApi<{ deliveries: Delivery[] }>('/api/v1/notifications/deliveries?limit=100')
    setRows(body.deliveries)
  }

  useEffect(() => {
    void reload().catch((err: unknown) => setError(describeError(err)))
    const timer = window.setInterval(() => {
      void reload().catch(() => {
        // 保留上一屏
      })
    }, 4000)
    return () => window.clearInterval(timer)
  }, [])

  async function retry(id: number) {
    await studioApi(`/api/v1/notifications/deliveries/${id}/retry`, { method: 'POST' })
    toast.success('已重试')
    await reload()
  }

  return (
    <PageShell title='通知记录' description='每次发送的结果。失败后可以再试一次，通道密钥不会出现在这里。'>
      {error ? <p className='mb-3 text-sm text-destructive'>{error}</p> : null}
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>时间</TableHead>
            <TableHead>类型</TableHead>
            <TableHead>设备</TableHead>
            <TableHead>状态</TableHead>
            <TableHead>摘要</TableHead>
            <TableHead />
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.length === 0 ? (
            <TableRow>
              <TableCell colSpan={6} className='text-muted-foreground'>
                还没有通知记录。可以在「通知」页发送一条测试消息。
              </TableCell>
            </TableRow>
          ) : null}
          {rows.map((row) => (
            <TableRow key={row.id}>
              <TableCell className='whitespace-nowrap text-xs'>{formatTime(row.createdUnixMs)}</TableCell>
              <TableCell>{kindLabels[row.kind] ?? row.kind}</TableCell>
              <TableCell>
                <div>{row.deviceId || '—'}</div>
                <div className='text-xs text-muted-foreground'>{row.code}</div>
              </TableCell>
              <TableCell>
                <Badge variant={row.status === 'sent' ? 'default' : row.status === 'failed' ? 'destructive' : 'secondary'}>
                  {row.status === 'sent' ? '已送达' : row.status === 'failed' ? '失败' : '待发送'}
                </Badge>
                <div className='mt-1 text-xs text-muted-foreground'>尝试 {row.attempts} 次</div>
                {row.lastError ? <div className='text-xs text-destructive'>{row.lastError}</div> : null}
              </TableCell>
              <TableCell className='max-w-md text-xs whitespace-pre-wrap'>{row.summary}</TableCell>
              <TableCell>
                {row.status !== 'sent' ? (
                  <Button size='sm' variant='outline' disabled={!writable} onClick={() => void retry(row.id).catch((err: unknown) => toast.error(describeError(err)))}>
                    重试
                  </Button>
                ) : null}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </PageShell>
  )
}
