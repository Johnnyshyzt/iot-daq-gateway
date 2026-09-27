import { useRef, useState } from 'react'
import { toast } from 'sonner'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { describeError, studioApi } from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'
import { PageShell } from './page-shell'
import { type LicenseSnapshot, useLicense } from './license-banner'

const featureTitle: Record<string, string> = {
  opcua: 'OPC UA 服务器',
  'http-push': 'HTTP 推送',
  'alarm-notifications': '报警通知',
  'scheduled-reports': '定时报表',
  'query-api': '只读查询接口',
}

const statusLabel: Record<string, string> = {
  community: '社区版',
  valid: '有效',
  grace: '宽限期',
  expired: '已过期',
  invalid: '无效',
}

export function LicensePage() {
  const admin = useAuthStore((state) => state.auth.user?.role[0]) === 'admin'
  const initial = useLicense()
  const [license, setLicense] = useState<LicenseSnapshot | null>(null)
  const view = license ?? initial
  const fileRef = useRef<HTMLInputElement>(null)
  const [message, setMessage] = useState('')

  async function upload(file: File) {
    const token = useAuthStore.getState().auth.accessToken
    const body = new FormData()
    body.set('file', file)
    const response = await fetch('/api/v1/license', {
      method: 'POST',
      headers: token ? { Authorization: `Bearer ${token}` } : {},
      body,
    })
    const payload = (await response.json()) as LicenseSnapshot & { message?: string }
    if (!response.ok) throw new Error(payload.message || '导入失败')
    setLicense(payload)
    setMessage(payload.message)
    toast.success('许可证已导入')
  }

  async function remove() {
    const next = await studioApi<LicenseSnapshot>('/api/v1/license', { method: 'DELETE' })
    setLicense(next)
    setMessage('已恢复社区版。采集不停止。')
    toast.success('已移除许可证')
  }

  async function copyFingerprint() {
    if (!view) return
    await navigator.clipboard.writeText(view.machineFingerprint)
    toast.success('机器指纹已复制，可发给厂商')
  }

  return (
    <PageShell
      title='授权许可'
      description='没有许可证时按社区版运行。上限和需要授权的功能写在配置里，不代表定价。采集不会因为授权失败而停止。'
    >
      {!view ? (
        <p className='text-sm text-muted-foreground'>正在读取授权…</p>
      ) : (
        <div className='grid gap-4 lg:grid-cols-2'>
          <Card>
            <CardHeader>
              <CardTitle className='flex items-center gap-2'>
                当前版本
                <Badge variant={view.edition === 'commercial' ? 'default' : 'secondary'}>
                  {view.edition === 'commercial' ? '商用' : '社区版'}
                </Badge>
                <Badge variant='outline'>{statusLabel[view.status] ?? view.status}</Badge>
              </CardTitle>
            </CardHeader>
            <CardContent className='space-y-3 text-sm'>
              <p>{view.message}</p>
              <p>客户：{view.customer || '（未登记）'}</p>
              <p>签发：{formatTime(view.issuedAt)}</p>
              <p>到期：{view.expiresAt ? formatTime(view.expiresAt) : '不限期'}</p>
              <p>宽限：{view.graceDays} 天{view.graceUntil ? `，至 ${formatTime(view.graceUntil)}` : ''}</p>
              <p className='text-muted-foreground'>采集继续运行。过期后不能再把设备或点位加过当前上限。</p>
              {message ? <p>{message}</p> : null}
              <div className='flex flex-wrap gap-2'>
                <Button disabled={!admin} onClick={() => fileRef.current?.click()}>
                  导入许可证
                </Button>
                <Button variant='outline' disabled={!admin || view.edition === 'community'} onClick={() => void remove().catch((error: unknown) => setMessage(describeError(error)))}>
                  移除许可证
                </Button>
                <input
                  ref={fileRef}
                  type='file'
                  accept='.json,application/json'
                  className='hidden'
                  onChange={(event) => {
                    const file = event.target.files?.[0]
                    event.target.value = ''
                    if (file) void upload(file).catch((error: unknown) => setMessage(describeError(error)))
                  }}
                />
              </div>
              {!admin ? <p className='text-muted-foreground'>只有管理员可以导入或移除。</p> : null}
            </CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle>用量和上限</CardTitle>
            </CardHeader>
            <CardContent className='space-y-4 text-sm'>
              <Meter label='设备（草稿）' used={view.draftDevices} limit={view.deviceLimit} />
              <Meter label='启用点位（草稿）' used={view.draftPoints} limit={view.pointLimit} />
              <p className='text-muted-foreground'>
                已发布：{view.publishedDevices} 台设备，{view.publishedPoints} 个启用点位。
              </p>
            </CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle>功能</CardTitle>
            </CardHeader>
            <CardContent className='space-y-2 text-sm'>
              {view.gatedFeatures.map((feature) => (
                <div key={feature} className='flex items-center justify-between gap-3'>
                  <span>{featureTitle[feature] ?? feature}</span>
                  <Badge variant={view.entitlements[feature] ? 'default' : 'outline'}>
                    {view.entitlements[feature] ? '可用' : '未授权'}
                  </Badge>
                </div>
              ))}
              <p className='text-muted-foreground'>清单在 Licensing:GatedFeatures。不在清单里的功能不拦截。</p>
            </CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle>机器指纹</CardTitle>
            </CardHeader>
            <CardContent className='space-y-3 text-sm'>
              <p className='text-muted-foreground'>把下面的指纹发给厂商，用于可选的机器绑定。它是哈希，不是可信平台模块证明。</p>
              <code className='block break-all rounded-md bg-muted px-3 py-2 font-mono text-xs'>{view.machineFingerprint}</code>
              <Button variant='outline' onClick={() => void copyFingerprint()}>
                复制指纹
              </Button>
              {view.boundFingerprint ? <p>许可证绑定：{view.boundFingerprint}</p> : <p>当前许可证不绑定机器。</p>}
            </CardContent>
          </Card>
        </div>
      )}
    </PageShell>
  )
}

function Meter({ label, used, limit }: { label: string; used: number; limit: number | null }) {
  const ratio = limit && limit > 0 ? Math.min(100, Math.round((used / limit) * 100)) : 8
  return (
    <div>
      <div className='mb-1 flex justify-between'>
        <span>{label}</span>
        <span>
          {used} / {limit == null ? '不限制' : limit}
        </span>
      </div>
      <div className='h-2 rounded-full bg-muted'>
        <div className='h-2 rounded-full bg-primary' style={{ width: `${ratio}%` }} />
      </div>
    </div>
  )
}

function formatTime(value?: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return date.toISOString().replace('.000Z', 'Z')
}
