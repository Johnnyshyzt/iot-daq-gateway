import { useEffect, useState } from 'react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Switch } from '@/components/ui/switch'
import { canWrite, describeError, studioApi } from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'
import { FeatureLock } from './license-banner'
import { PageShell } from './page-shell'

type Header = { name: string; value: string }

type Target = {
  id: string
  name: string
  enabled: boolean
  url: string
  method: string
  headers: Header[]
  authKind: string
  authUser: string
  hasSecret: boolean
  signatureHeader: string
  sendValues: boolean
  valueMode: string
  periodicSeconds: number
  sendStatus: boolean
  sendAlarms: boolean
  batchMax: number
  batchIntervalMs: number
  timeoutMs: number
  maxRetries: number
  backoffInitialMs: number
  backoffMaxMs: number
  delivered: number
  failed: number
  dropped: number
  spoolDepth: number
  lastError: string
}

type Draft = Target & { secret: string; clearSecret: boolean }

const empty = (): Draft => ({
  id: '',
  name: '',
  enabled: true,
  url: 'https://',
  method: 'POST',
  headers: [],
  authKind: 'none',
  authUser: '',
  hasSecret: false,
  secret: '',
  clearSecret: false,
  signatureHeader: 'X-DAQ-Signature',
  sendValues: true,
  valueMode: 'change',
  periodicSeconds: 30,
  sendStatus: true,
  sendAlarms: true,
  batchMax: 50,
  batchIntervalMs: 1000,
  timeoutMs: 8000,
  maxRetries: 8,
  backoffInitialMs: 1000,
  backoffMaxMs: 60000,
  delivered: 0,
  failed: 0,
  dropped: 0,
  spoolDepth: 0,
  lastError: '',
})

export function HttpPushPage() {
  const writable = canWrite(useAuthStore((state) => state.auth.user?.role[0]))
  const [targets, setTargets] = useState<Target[]>([])
  const [draft, setDraft] = useState<Draft>(empty())
  const [headerName, setHeaderName] = useState('')
  const [headerValue, setHeaderValue] = useState('')

  async function load() {
    const body = await studioApi<{ targets: Target[] }>('/api/v1/http-push')
    setTargets(body.targets)
  }

  useEffect(() => {
    void load().catch((error: unknown) => toast.error(describeError(error)))
    const timer = window.setInterval(() => void load().catch(() => undefined), 3000)
    return () => window.clearInterval(timer)
  }, [])

  async function save() {
    const saved = await studioApi<Target>('/api/v1/http-push', {
      method: 'PUT',
      body: JSON.stringify({
        ...draft,
        headers: draft.headers,
      }),
    })
    setDraft({ ...empty(), ...saved, secret: '', clearSecret: false })
    await load()
    toast.success('HTTP 推送已保存')
  }

  return (
    <PageShell
      title='北向 HTTP'
      description='把契约 v1 批次推到 MES 或云端。目标不可达时写入磁盘缓冲，恢复后按顺序补发。密钥不会再显示。'
    >
      <FeatureLock feature='http-push' />
      <div className='mb-4 grid gap-3 md:grid-cols-3'>
        {targets.map((target) => (
          <Card key={target.id}>
            <CardHeader>
              <CardTitle className='text-base'>{target.name}</CardTitle>
            </CardHeader>
            <CardContent className='space-y-1 text-sm text-muted-foreground'>
              <div>{target.enabled ? '启用' : '停用'} · {target.method} · {target.authKind}</div>
              <div className='break-all'>{target.url}</div>
              <div>成功 {target.delivered} · 失败 {target.failed} · 缓冲 {target.spoolDepth} · 丢弃 {target.dropped}</div>
              {target.lastError ? <div className='text-destructive'>{target.lastError}</div> : null}
              <div className='flex gap-2 pt-2'>
                <Button size='sm' variant='outline' disabled={!writable} onClick={() => setDraft({ ...empty(), ...target, secret: '', clearSecret: false })}>
                  编辑
                </Button>
                <Button
                  size='sm'
                  variant='outline'
                  disabled={!writable}
                  onClick={() =>
                    void studioApi(`/api/v1/http-push/${target.id}/test`, { method: 'POST' })
                      .then(() => toast.success('测试已送达'))
                      .catch((error: unknown) => toast.error(describeError(error)))
                  }
                >
                  测试
                </Button>
                <Button
                  size='sm'
                  variant='outline'
                  disabled={!writable}
                  onClick={() =>
                    void studioApi(`/api/v1/http-push/${target.id}`, { method: 'DELETE' })
                      .then(load)
                      .then(() => toast.success('已删除'))
                      .catch((error: unknown) => toast.error(describeError(error)))
                  }
                >
                  删除
                </Button>
              </div>
            </CardContent>
          </Card>
        ))}
        {targets.length === 0 ? <p className='text-sm text-muted-foreground'>还没有 HTTP 推送目标。</p> : null}
      </div>
      <Card>
        <CardHeader>
          <CardTitle>{draft.id ? '编辑目标' : '新建目标'}</CardTitle>
        </CardHeader>
        <CardContent className='grid gap-3 sm:grid-cols-2'>
          <Field label='名称'>
            <Input value={draft.name} disabled={!writable} onChange={(event) => setDraft({ ...draft, name: event.target.value })} />
          </Field>
          <Field label='URL'>
            <Input value={draft.url} disabled={!writable} onChange={(event) => setDraft({ ...draft, url: event.target.value })} />
          </Field>
          <Field label='方法'>
            <Select value={draft.method} disabled={!writable} onValueChange={(method) => setDraft({ ...draft, method })}>
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value='POST'>POST</SelectItem>
                <SelectItem value='PUT'>PUT</SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <Field label='认证'>
            <Select value={draft.authKind} disabled={!writable} onValueChange={(authKind) => setDraft({ ...draft, authKind })}>
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value='none'>无</SelectItem>
                <SelectItem value='basic'>Basic</SelectItem>
                <SelectItem value='bearer'>Bearer</SelectItem>
                <SelectItem value='hmac-sha256'>HMAC-SHA256</SelectItem>
              </SelectContent>
            </Select>
          </Field>
          {draft.authKind === 'basic' ? (
            <Field label='用户名'>
              <Input value={draft.authUser} disabled={!writable} onChange={(event) => setDraft({ ...draft, authUser: event.target.value })} />
            </Field>
          ) : null}
          {draft.authKind !== 'none' ? (
            <Field label={draft.hasSecret ? '密钥（留空保持原值）' : '密钥'}>
              <Input type='password' value={draft.secret} disabled={!writable} placeholder={draft.hasSecret ? '已设置' : ''} onChange={(event) => setDraft({ ...draft, secret: event.target.value, clearSecret: false })} />
            </Field>
          ) : null}
          {draft.authKind === 'hmac-sha256' ? (
            <Field label='签名头'>
              <Input value={draft.signatureHeader} disabled={!writable} onChange={(event) => setDraft({ ...draft, signatureHeader: event.target.value })} />
            </Field>
          ) : null}
          <Field label='点位发送'>
            <Select value={draft.sendValues ? draft.valueMode : 'off'} disabled={!writable} onValueChange={(value) => setDraft({ ...draft, sendValues: value !== 'off', valueMode: value === 'periodic' ? 'periodic' : 'change' })}>
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value='off'>不发送</SelectItem>
                <SelectItem value='change'>变化时</SelectItem>
                <SelectItem value='periodic'>周期批次</SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <Field label='周期（秒）'>
            <Input type='number' value={draft.periodicSeconds} disabled={!writable} onChange={(event) => setDraft({ ...draft, periodicSeconds: Number(event.target.value) })} />
          </Field>
          <Field label='每批条数'>
            <Input type='number' value={draft.batchMax} disabled={!writable} onChange={(event) => setDraft({ ...draft, batchMax: Number(event.target.value) })} />
          </Field>
          <Field label='超时（毫秒）'>
            <Input type='number' value={draft.timeoutMs} disabled={!writable} onChange={(event) => setDraft({ ...draft, timeoutMs: Number(event.target.value) })} />
          </Field>
          <Field label='退避起始（毫秒）'>
            <Input type='number' value={draft.backoffInitialMs} disabled={!writable} onChange={(event) => setDraft({ ...draft, backoffInitialMs: Number(event.target.value) })} />
          </Field>
          <Field label='退避上限（毫秒）'>
            <Input type='number' value={draft.backoffMaxMs} disabled={!writable} onChange={(event) => setDraft({ ...draft, backoffMaxMs: Number(event.target.value) })} />
          </Field>
          <div className='flex items-center justify-between'>
            <Label>启用</Label>
            <Switch checked={draft.enabled} disabled={!writable} onCheckedChange={(enabled) => setDraft({ ...draft, enabled })} />
          </div>
          <div className='flex items-center justify-between'>
            <Label>发送连接状态</Label>
            <Switch checked={draft.sendStatus} disabled={!writable} onCheckedChange={(sendStatus) => setDraft({ ...draft, sendStatus })} />
          </div>
          <div className='flex items-center justify-between'>
            <Label>发送报警</Label>
            <Switch checked={draft.sendAlarms} disabled={!writable} onCheckedChange={(sendAlarms) => setDraft({ ...draft, sendAlarms })} />
          </div>
          <div className='sm:col-span-2 space-y-2'>
            <Label>额外请求头</Label>
            <div className='flex flex-wrap gap-2'>
              {draft.headers.map((header) => (
                <Button key={header.name} type='button' size='sm' variant='outline' disabled={!writable} onClick={() => setDraft({ ...draft, headers: draft.headers.filter((item) => item.name !== header.name) })}>
                  {header.name}
                </Button>
              ))}
            </div>
            <div className='flex gap-2'>
              <Input placeholder='头名称' value={headerName} disabled={!writable} onChange={(event) => setHeaderName(event.target.value)} />
              <Input placeholder='值' value={headerValue} disabled={!writable} onChange={(event) => setHeaderValue(event.target.value)} />
              <Button
                type='button'
                variant='outline'
                disabled={!writable || !headerName.trim()}
                onClick={() => {
                  setDraft({ ...draft, headers: [...draft.headers.filter((item) => item.name !== headerName.trim()), { name: headerName.trim(), value: headerValue }] })
                  setHeaderName('')
                  setHeaderValue('')
                }}
              >
                添加
              </Button>
            </div>
          </div>
          <div className='sm:col-span-2'>
            <Button disabled={!writable} onClick={() => void save().catch((error: unknown) => toast.error(describeError(error)))}>
              保存
            </Button>
          </div>
        </CardContent>
      </Card>
    </PageShell>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className='grid gap-1.5'>
      <Label>{label}</Label>
      {children}
    </div>
  )
}
