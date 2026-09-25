import { useEffect, useState } from 'react'
import { Link } from '@tanstack/react-router'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { studioApi, type ConfigView, type RuntimeStatus } from '@/lib/studio-api'
import { PageShell } from './page-shell'

export function OverviewPage() {
  const [config, setConfig] = useState<ConfigView | null>(null)
  const [status, setStatus] = useState<RuntimeStatus | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    let stop = false
    async function load() {
      try {
        const [nextConfig, nextStatus] = await Promise.all([
          studioApi<ConfigView>('/api/v1/config'),
          studioApi<RuntimeStatus>('/api/v1/runtime/status'),
        ])
        if (!stop) {
          setConfig(nextConfig)
          setStatus(nextStatus)
          setError('')
        }
      } catch (err) {
        if (!stop) setError(err instanceof Error ? err.message : '加载失败')
      }
    }
    void load()
    const timer = window.setInterval(() => void load(), 4000)
    return () => {
      stop = true
      window.clearInterval(timer)
    }
  }, [])

  const online =
    status?.devices.filter((device) => device.status === 'online').length ?? 0

  return (
    <PageShell
      title='概览'
      description='站点、修订和设备在线情况。发布后的配置由 Gateway.Host 读取。'
      actions={
        <Button asChild>
          <Link to='/publish'>去发布</Link>
        </Button>
      }
    >
      {error ? <p className='mb-4 text-sm text-destructive'>{error}</p> : null}
      <div className='grid gap-4 sm:grid-cols-2 xl:grid-cols-4'>
        <Card>
          <CardHeader>
            <CardDescription>站点</CardDescription>
            <CardTitle>{status?.name || config?.draft.gateway.metadata.name || '—'}</CardTitle>
          </CardHeader>
          <CardContent className='text-sm text-muted-foreground'>
            {status?.siteId || config?.draft.gateway.metadata.siteId}
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardDescription>运行态</CardDescription>
            <CardTitle>{status?.mode === 'live' ? '已连接网关' : '模拟'}</CardTitle>
          </CardHeader>
          <CardContent>
            <Badge variant={status?.mode === 'live' ? 'default' : 'secondary'}>
              {status?.mode || '…'}
            </Badge>
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardDescription>在线设备</CardDescription>
            <CardTitle>
              {online} / {status?.devices.length ?? 0}
            </CardTitle>
          </CardHeader>
          <CardContent className='text-sm text-muted-foreground'>
            {config?.dirty ? '草稿尚未发布' : '草稿已与发布对齐'}
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardDescription>当前修订</CardDescription>
            <CardTitle className='truncate text-base'>
              {(status?.activeRevision || config?.activeRevision || '—').slice(0, 12)}
            </CardTitle>
          </CardHeader>
          <CardContent className='text-sm text-muted-foreground'>
            完整哈希在发布页
          </CardContent>
        </Card>
      </div>
    </PageShell>
  )
}
