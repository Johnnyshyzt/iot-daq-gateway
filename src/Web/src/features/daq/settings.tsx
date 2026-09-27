import { useEffect, useState } from 'react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { ChangePasswordForm } from '@/features/auth/change-password-form'
import { canWrite, describeError, roleLabel, studioApi } from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'
import { VizSettingsCard } from '@/features/viz/viz-settings'
import { BackupCard } from './backup-card'
import { PageShell } from './page-shell'

type SettingsView = {
  siteId: string
  name: string
  logLevel: string
  programWrite: boolean
  defaultIntervalMs: number
  changeOnly: boolean
  dataDirectory: string
  license: { enforced: boolean; message: string }
  currentUser: { username: string; role: string; mustChangePassword?: boolean }
  users: Array<{ username: string; role: string; mustChangePassword?: boolean }>
  accountMode?: string
  accountMessage?: string
}

export function SettingsPage() {
  const writable = canWrite(useAuthStore((state) => state.auth.user?.role[0]))
  const [settings, setSettings] = useState<SettingsView | null>(null)
  const [message, setMessage] = useState('')

  useEffect(() => {
    void studioApi<SettingsView>('/api/v1/settings')
      .then(setSettings)
      .catch((error: unknown) => setMessage(describeError(error)))
  }, [])

  async function save() {
    if (!settings) return
    if (!/^[A-Za-z0-9_-]+$/.test(settings.siteId.trim())) {
      setMessage('站点标识只能包含字母、数字、下划线和连字符')
      return
    }
    if (!settings.name.trim() || settings.name.trim().length > 80) {
      setMessage('站点名称不能为空，且不超过 80 个字符')
      return
    }
    if (!Number.isFinite(settings.defaultIntervalMs) || settings.defaultIntervalMs < 100 || settings.defaultIntervalMs > 86_400_000) {
      setMessage('默认采集周期需在 100 到 86400000 毫秒之间')
      return
    }
    await studioApi('/api/v1/settings', {
      method: 'PUT',
      body: JSON.stringify({
        siteId: settings.siteId,
        name: settings.name,
        logLevel: settings.logLevel,
        programWrite: settings.programWrite,
        defaultIntervalMs: settings.defaultIntervalMs,
        changeOnly: settings.changeOnly,
      }),
    })
    setMessage('已写入网关草稿，发布后才会影响运行中的主题')
    toast.success('系统设置已保存到草稿')
  }

  if (!settings) {
    return (
      <PageShell title='系统'>
        <p className='text-sm text-muted-foreground'>{message || '加载中…'}</p>
      </PageShell>
    )
  }

  return (
    <PageShell
      title='系统'
      description='站点名写入 Gateway 草稿，发布后才影响运行中的主题。账号只存在本机。数据库备份只有管理员可以下载。'
    >
      <div className='grid gap-4 lg:grid-cols-2'>
        <Card>
          <CardHeader>
            <CardTitle>站点</CardTitle>
          </CardHeader>
          <CardContent className='grid gap-3'>
            <Field label='站点 id'>
              <Input
                value={settings.siteId}
                disabled={!writable}
                onChange={(event) => setSettings({ ...settings, siteId: event.target.value })}
              />
            </Field>
            <Field label='名称'>
              <Input
                value={settings.name}
                disabled={!writable}
                onChange={(event) => setSettings({ ...settings, name: event.target.value })}
              />
            </Field>
            <Field label='默认扫描周期 ms'>
              <Input
                type='number'
                value={settings.defaultIntervalMs}
                disabled={!writable}
                onChange={(event) =>
                  setSettings({ ...settings, defaultIntervalMs: Number(event.target.value) })
                }
              />
            </Field>
            <div className='flex items-center justify-between'>
              <Label>仅变化时发布</Label>
              <Switch
                checked={settings.changeOnly}
                disabled={!writable}
                onCheckedChange={(changeOnly) => setSettings({ ...settings, changeOnly })}
              />
            </div>
            <Button disabled={!writable} onClick={() => void save().catch((error: unknown) => setMessage(describeError(error)))}>
              保存草稿
            </Button>
            {message ? <p className='text-sm text-muted-foreground'>{message}</p> : null}
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>账号与许可证</CardTitle>
          </CardHeader>
          <CardContent className='space-y-2 text-sm'>
            <p>
              当前用户：{settings.currentUser.username} · {roleLabel(settings.currentUser.role)}
              {settings.currentUser.mustChangePassword ? ' · 必须修改密码' : ''}
            </p>
            <p>数据目录：{settings.dataDirectory}</p>
            {settings.accountMessage ? <p>{settings.accountMessage}</p> : null}
            <p>{settings.license.message}</p>
            {settings.users.length > 0 ? (
              <ul>
                {settings.users.map((user) => (
                  <li key={user.username}>
                    {user.username} · {roleLabel(user.role)}
                    {user.mustChangePassword ? ' · 待修改密码' : ''}
                  </li>
                ))}
              </ul>
            ) : null}
          </CardContent>
        </Card>
        <VizSettingsCard />
        <BackupCard />
        <ReliabilityCard writable={writable} />
        <Card className='lg:col-span-2'>
          <CardHeader>
            <CardTitle>修改密码</CardTitle>
          </CardHeader>
          <CardContent className='max-w-md'>
            <ChangePasswordForm />
          </CardContent>
        </Card>
      </div>
    </PageShell>
  )
}

function ReliabilityCard({ writable }: { writable: boolean }) {
  const [form, setForm] = useState({
    reconnectInitialSeconds: 2,
    reconnectMultiplier: 2,
    reconnectCapSeconds: 120,
    reconnectJitter: 0.2,
    stallSeconds: 30,
    spoolMaxMessages: 10000,
    spoolMaxAgeHours: 24,
    spoolMaxMegabytes: 64,
  })
  const [note, setNote] = useState('')

  useEffect(() => {
    void studioApi<typeof form>('/api/v1/ops/reliability')
      .then(setForm)
      .catch((error: unknown) => setNote(describeError(error)))
  }, [])

  async function save() {
    await studioApi('/api/v1/ops/reliability', { method: 'PUT', body: JSON.stringify(form) })
    setNote('已保存。重连上限和缓冲限制马上生效，不必重启。')
    toast.success('可靠性设置已保存')
  }

  return (
    <Card className='lg:col-span-2'>
      <CardHeader>
        <CardTitle>重连与 MQTT 缓冲</CardTitle>
      </CardHeader>
      <CardContent className='grid gap-3 sm:grid-cols-2 xl:grid-cols-4'>
        <Field label='首次重连（秒）'>
          <Input type='number' disabled={!writable} value={form.reconnectInitialSeconds} onChange={(event) => setForm({ ...form, reconnectInitialSeconds: Number(event.target.value) })} />
        </Field>
        <Field label='倍数'>
          <Input type='number' disabled={!writable} value={form.reconnectMultiplier} onChange={(event) => setForm({ ...form, reconnectMultiplier: Number(event.target.value) })} />
        </Field>
        <Field label='上限（秒）'>
          <Input type='number' disabled={!writable} value={form.reconnectCapSeconds} onChange={(event) => setForm({ ...form, reconnectCapSeconds: Number(event.target.value) })} />
        </Field>
        <Field label='抖动比例'>
          <Input type='number' disabled={!writable} value={form.reconnectJitter} onChange={(event) => setForm({ ...form, reconnectJitter: Number(event.target.value) })} />
        </Field>
        <Field label='停滞判定（秒）'>
          <Input type='number' disabled={!writable} value={form.stallSeconds} onChange={(event) => setForm({ ...form, stallSeconds: Number(event.target.value) })} />
        </Field>
        <Field label='缓冲条数上限'>
          <Input type='number' disabled={!writable} value={form.spoolMaxMessages} onChange={(event) => setForm({ ...form, spoolMaxMessages: Number(event.target.value) })} />
        </Field>
        <Field label='缓冲保留（小时）'>
          <Input type='number' disabled={!writable} value={form.spoolMaxAgeHours} onChange={(event) => setForm({ ...form, spoolMaxAgeHours: Number(event.target.value) })} />
        </Field>
        <Field label='缓冲大小（MB）'>
          <Input type='number' disabled={!writable} value={form.spoolMaxMegabytes} onChange={(event) => setForm({ ...form, spoolMaxMegabytes: Number(event.target.value) })} />
        </Field>
        <div className='sm:col-span-2 xl:col-span-4'>
          <Button disabled={!writable} onClick={() => void save().catch((error: unknown) => setNote(describeError(error)))}>
            保存可靠性设置
          </Button>
          {note ? <p className='mt-2 text-sm text-muted-foreground'>{note}</p> : null}
        </div>
      </CardContent>
    </Card>
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
