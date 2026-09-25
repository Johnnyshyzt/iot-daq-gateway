import { useEffect, useState } from 'react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { canWrite, describeError, roleLabel, studioApi } from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'
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
  currentUser: { username: string; role: string }
  users: Array<{ username: string; role: string }>
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
    <PageShell title='系统' description='站点名写入 Gateway 草稿。许可证桩当前不拦截管理 API。'>
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
            </p>
            <p>数据目录：{settings.dataDirectory}</p>
            <p>{settings.license.message}</p>
            {settings.users.length > 0 ? (
              <ul>
                {settings.users.map((user) => (
                  <li key={user.username}>
                    {user.username} · {roleLabel(user.role)}
                  </li>
                ))}
              </ul>
            ) : null}
          </CardContent>
        </Card>
      </div>
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
