import { useEffect, useState } from 'react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { canWrite, describeError, studioApi } from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'
import { FeatureLock } from './license-banner'
import { PageShell } from './page-shell'

type ApiKey = {
  id: string
  name: string
  prefix: string
  createdUnixMs: number
  createdBy: string
  revokedUnixMs: number | null
  lastUsedUnixMs: number | null
}

export function QueryApiPage() {
  const writable = canWrite(useAuthStore((state) => state.auth.user?.role[0]))
  const [keys, setKeys] = useState<ApiKey[]>([])
  const [name, setName] = useState('')
  const [once, setOnce] = useState('')

  async function load() {
    const body = await studioApi<{ keys: ApiKey[] }>('/api/v1/api-keys')
    setKeys(body.keys)
  }

  useEffect(() => {
    void load().catch((error: unknown) => toast.error(describeError(error)))
  }, [])

  return (
    <PageShell
      title='查询接口'
      description='给 MES / SCADA 的只读 HTTP 接口。密钥只在创建时显示一次，库里只存 SHA-256。OpenAPI 文档不需要登录。'
      actions={
        <Button variant='outline' asChild>
          <a href='/api/query/v1/openapi.json' target='_blank' rel='noreferrer'>
            打开 OpenAPI
          </a>
        </Button>
      }
    >
      <FeatureLock feature='query-api' />
      <Card className='mb-4'>
        <CardHeader>
          <CardTitle>新建只读密钥</CardTitle>
        </CardHeader>
        <CardContent className='flex flex-wrap items-end gap-3'>
          <div className='grid gap-1.5'>
            <Label>名称</Label>
            <Input value={name} disabled={!writable} placeholder='例如 MES 线边' onChange={(event) => setName(event.target.value)} />
          </div>
          <Button
            disabled={!writable || !name.trim()}
            onClick={() =>
              void studioApi<{ key: string }>('/api/v1/api-keys', {
                method: 'POST',
                body: JSON.stringify({ name: name.trim() }),
              })
                .then((body) => {
                  setOnce(body.key)
                  setName('')
                  return load()
                })
                .catch((error: unknown) => toast.error(describeError(error)))
            }
          >
            创建
          </Button>
        </CardContent>
      </Card>
      {once ? (
        <Card className='mb-4 border-primary'>
          <CardHeader>
            <CardTitle>请立即复制</CardTitle>
          </CardHeader>
          <CardContent>
            <p className='mb-2 text-sm text-muted-foreground'>关闭或刷新后无法再查看完整密钥。请求头使用 X-Api-Key，或 Authorization: Bearer。</p>
            <pre className='overflow-auto rounded-md bg-muted p-3 text-sm'>{once}</pre>
          </CardContent>
        </Card>
      ) : null}
      <Card>
        <CardHeader>
          <CardTitle>已有密钥</CardTitle>
        </CardHeader>
        <CardContent className='space-y-3'>
          {keys.length === 0 ? <p className='text-sm text-muted-foreground'>还没有查询密钥。</p> : null}
          {keys.map((item) => (
            <div key={item.id} className='flex flex-wrap items-center justify-between gap-2 border-b pb-2 text-sm'>
              <div>
                <div className='font-medium'>{item.name}</div>
                <div className='font-mono text-xs text-muted-foreground'>{item.prefix}… · {item.revokedUnixMs ? '已吊销' : '有效'}</div>
              </div>
              <Button
                size='sm'
                variant='outline'
                disabled={!writable || item.revokedUnixMs !== null}
                onClick={() =>
                  void studioApi(`/api/v1/api-keys/${item.id}/revoke`, { method: 'POST' })
                    .then(load)
                    .then(() => toast.success('已吊销'))
                    .catch((error: unknown) => toast.error(describeError(error)))
                }
              >
                吊销
              </Button>
            </div>
          ))}
          <p className='text-sm text-muted-foreground'>
            接口：GET /api/query/v1/devices、/devices/&#123;id&#125;/values、/devices/&#123;id&#125;/history、/alarms、/utilization。计算点多一个 computed。授权包含 OEE 时，稼动摘要额外带可用率、性能率、质量率、OEE 和标记。契约文件在 /api/contract/v1。
          </p>
        </CardContent>
      </Card>
    </PageShell>
  )
}
