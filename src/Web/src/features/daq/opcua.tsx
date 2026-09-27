import { useEffect, useState } from 'react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { canWrite, describeError, studioApi } from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'
import { PageShell } from './page-shell'

type Settings = {
  enabled: boolean
  port: number
  allowAnonymous: boolean
  allowNone: boolean
  allowSignAndEncrypt: boolean
  username: string
  hasPassword: boolean
}

type Runtime = {
  enabled: boolean
  listening: boolean
  port: number
  endpoint: string
  lastError: string
}

export function OpcUaPage() {
  const writable = canWrite(useAuthStore((state) => state.auth.user?.role[0]))
  const [settings, setSettings] = useState<Settings | null>(null)
  const [runtime, setRuntime] = useState<Runtime | null>(null)
  const [password, setPassword] = useState('')

  async function load() {
    const body = await studioApi<{ settings: Settings; runtime: Runtime }>('/api/v1/opcua')
    setSettings(body.settings)
    setRuntime(body.runtime)
  }

  useEffect(() => {
    void load().catch((error: unknown) => toast.error(describeError(error)))
    const timer = window.setInterval(() => void load().catch(() => undefined), 3000)
    return () => window.clearInterval(timer)
  }, [])

  if (!settings) {
    return (
      <PageShell title='OPC UA'>
        <p className='text-sm text-muted-foreground'>正在读取 OPC UA 设置…</p>
      </PageShell>
    )
  }

  return (
    <PageShell
      title='OPC UA'
      description='把车间、产线和设备展开成 OPC UA 地址空间。默认关闭。演示可用 None，对接用 Basic256Sha256 签名加密。'
    >
      <Card className='mb-4'>
        <CardHeader>
          <CardTitle>运行</CardTitle>
        </CardHeader>
        <CardContent className='text-sm'>
          {runtime?.listening ? `正在监听 ${runtime.endpoint}` : settings.enabled ? '已启用，尚未监听' : '未启用'}
          {runtime?.lastError ? <div className='mt-1 text-destructive'>{runtime.lastError}</div> : null}
        </CardContent>
      </Card>
      <Card>
        <CardHeader>
          <CardTitle>服务器</CardTitle>
        </CardHeader>
        <CardContent className='grid gap-3 sm:grid-cols-2'>
          <div className='flex items-center justify-between sm:col-span-2'>
            <Label>启用</Label>
            <Switch checked={settings.enabled} disabled={!writable} onCheckedChange={(enabled) => setSettings({ ...settings, enabled })} />
          </div>
          <div className='grid gap-1.5'>
            <Label>端口</Label>
            <Input type='number' value={settings.port} disabled={!writable} onChange={(event) => setSettings({ ...settings, port: Number(event.target.value) })} />
          </div>
          <div className='grid gap-1.5'>
            <Label>用户名（可空）</Label>
            <Input value={settings.username} disabled={!writable} onChange={(event) => setSettings({ ...settings, username: event.target.value })} />
          </div>
          <div className='grid gap-1.5'>
            <Label>{settings.hasPassword ? '密码（留空保持原值）' : '密码'}</Label>
            <Input type='password' value={password} disabled={!writable} placeholder={settings.hasPassword ? '已设置' : ''} onChange={(event) => setPassword(event.target.value)} />
          </div>
          <div className='flex items-center justify-between'>
            <Label>允许匿名</Label>
            <Switch checked={settings.allowAnonymous} disabled={!writable} onCheckedChange={(allowAnonymous) => setSettings({ ...settings, allowAnonymous })} />
          </div>
          <div className='flex items-center justify-between'>
            <Label>SecurityPolicy None</Label>
            <Switch checked={settings.allowNone} disabled={!writable} onCheckedChange={(allowNone) => setSettings({ ...settings, allowNone })} />
          </div>
          <div className='flex items-center justify-between sm:col-span-2'>
            <Label>Basic256Sha256 签名并加密</Label>
            <Switch checked={settings.allowSignAndEncrypt} disabled={!writable} onCheckedChange={(allowSignAndEncrypt) => setSettings({ ...settings, allowSignAndEncrypt })} />
          </div>
          <div className='sm:col-span-2'>
            <Button
              disabled={!writable}
              onClick={() =>
                void studioApi('/api/v1/opcua', {
                  method: 'PUT',
                  body: JSON.stringify({ ...settings, password }),
                })
                  .then(() => {
                    setPassword('')
                    return load()
                  })
                  .then(() => toast.success('OPC UA 设置已保存'))
                  .catch((error: unknown) => toast.error(describeError(error)))
              }
            >
              保存
            </Button>
            <p className='mt-3 text-sm text-muted-foreground'>
              端点 opc.tcp://本机:{settings.port}/iot-daq-gateway。应用证书在数据目录 opcua/pki/own。不受信任的客户端证书会出现在 opcua/pki/rejected，拷到 trusted 后重连即可。密码只存哈希。
            </p>
          </div>
        </CardContent>
      </Card>
    </PageShell>
  )
}
