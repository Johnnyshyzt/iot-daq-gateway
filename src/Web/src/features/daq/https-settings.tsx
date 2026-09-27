import { useEffect, useState } from 'react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { describeError, studioApi } from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'
import { PageShell } from './page-shell'

type HttpsView = {
  mode: string
  enabled: boolean
  httpPort: number
  httpsPort: number
  redirectHttp: boolean
  listenAny: boolean
  dnsNames: string[]
  ipAddresses: string[]
  subject: string
  thumbprint: string
  notAfter: string
  note: string
}

const modeText: Record<string, string> = {
  off: '关闭（HTTP）',
  'self-signed': '自签证书',
  imported: '已导入',
}

export function HttpsSettingsPage() {
  const [view, setView] = useState<HttpsView | null>(null)
  const [dns, setDns] = useState('localhost')
  const [ips, setIps] = useState('127.0.0.1')
  const [httpPort, setHttpPort] = useState(5080)
  const [httpsPort, setHttpsPort] = useState(5443)
  const [redirectHttp, setRedirectHttp] = useState(false)
  const [listenAny, setListenAny] = useState(false)
  const [password, setPassword] = useState('')
  const [pfx, setPfx] = useState<File | null>(null)
  const [pem, setPem] = useState<File | null>(null)
  const [key, setKey] = useState<File | null>(null)
  const [message, setMessage] = useState('')

  async function load() {
    const next = await studioApi<HttpsView>('/api/v1/ops/https')
    setView(next)
    setHttpPort(next.httpPort || 5080)
    setHttpsPort(next.httpsPort || 5443)
    setRedirectHttp(next.redirectHttp)
    setListenAny(next.listenAny)
    if (next.dnsNames?.length) setDns(next.dnsNames.join(', '))
    if (next.ipAddresses?.length) setIps(next.ipAddresses.join(', '))
  }

  useEffect(() => {
    void load().catch((error: unknown) => setMessage(describeError(error)))
  }, [])

  function split(value: string) {
    return value
      .split(/[,，\s]+/)
      .map((item) => item.trim())
      .filter(Boolean)
  }

  async function generate() {
    const next = await studioApi<HttpsView>('/api/v1/ops/https/self-signed', {
      method: 'POST',
      body: JSON.stringify({
        dnsNames: split(dns),
        ipAddresses: split(ips),
        httpPort,
        httpsPort,
        redirectHttp,
        listenAny,
      }),
    })
    setView(next)
    toast.success('证书已生成，重启后生效')
  }

  async function upload() {
    const token = useAuthStore.getState().auth.accessToken
    const body = new FormData()
    body.set('httpPort', String(httpPort))
    body.set('httpsPort', String(httpsPort))
    body.set('redirectHttp', redirectHttp ? 'true' : 'false')
    body.set('listenAny', listenAny ? 'true' : 'false')
    if (password) body.set('password', password)
    if (pem && key) {
      body.set('certificate', pem)
      body.set('key', key)
    } else if (pfx) {
      body.set('pfx', pfx)
    } else {
      setMessage('请选择 PFX，或同时选择 PEM 证书和私钥。')
      return
    }
    const response = await fetch('/api/v1/ops/https/import', {
      method: 'POST',
      headers: token ? { Authorization: `Bearer ${token}` } : {},
      body,
    })
    const payload = (await response.json()) as HttpsView & { message?: string }
    if (!response.ok) throw new Error(payload.message || '导入失败')
    setView(payload)
    toast.success('证书已导入，重启后生效')
  }

  async function disable() {
    const next = await studioApi<HttpsView>('/api/v1/ops/https/disable', { method: 'POST' })
    setView(next)
    toast.success('已关闭 HTTPS，重启后回到 HTTP')
  }

  return (
    <PageShell
      title='HTTPS'
      description='首次运行默认仍是 HTTP，方便现场先配好账号。证书保存后需要重启进程才会监听。'
    >
      {message ? <p className='mb-3 text-sm text-destructive'>{message}</p> : null}
      <div className='grid gap-4 lg:grid-cols-2' data-testid='https-settings'>
        <Card>
          <CardHeader>
            <CardTitle>当前绑定</CardTitle>
          </CardHeader>
          <CardContent className='space-y-2 text-sm'>
            <p>模式：{view ? (modeText[view.mode] ?? view.mode) : '读取中…'}</p>
            <p>HTTP {view?.httpPort ?? httpPort} · HTTPS {view?.httpsPort ?? httpsPort}</p>
            <p>跳转：{view?.redirectHttp ? 'HTTP 会跳到 HTTPS' : '不跳转'}</p>
            <p>监听：{view?.listenAny ? '所有网卡' : '仅本机'}</p>
            {view?.subject ? <p className='break-all'>主题：{view.subject}</p> : null}
            {view?.thumbprint ? <p className='break-all font-mono text-xs'>指纹：{view.thumbprint}</p> : null}
            {view?.notAfter ? <p>到期：{view.notAfter}</p> : null}
            <p className='text-muted-foreground'>{view?.note}</p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>生成自签证书</CardTitle>
          </CardHeader>
          <CardContent className='space-y-3'>
            <div>
              <Label>主机名（SAN）</Label>
              <Input value={dns} onChange={(event) => setDns(event.target.value)} />
            </div>
            <div>
              <Label>IP（SAN）</Label>
              <Input value={ips} onChange={(event) => setIps(event.target.value)} />
            </div>
            <div className='flex gap-3'>
              <div>
                <Label>HTTP 端口</Label>
                <Input type='number' value={httpPort} onChange={(event) => setHttpPort(Number(event.target.value))} />
              </div>
              <div>
                <Label>HTTPS 端口</Label>
                <Input type='number' value={httpsPort} onChange={(event) => setHttpsPort(Number(event.target.value))} />
              </div>
            </div>
            <div className='flex items-center justify-between'>
              <Label>HTTP 跳转到 HTTPS</Label>
              <Switch checked={redirectHttp} onCheckedChange={setRedirectHttp} />
            </div>
            <div className='flex items-center justify-between'>
              <Label>监听所有网卡</Label>
              <Switch checked={listenAny} onCheckedChange={setListenAny} />
            </div>
            <Button onClick={() => void generate().catch((error: unknown) => setMessage(describeError(error)))}>
              生成自签证书
            </Button>
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>导入证书</CardTitle>
          </CardHeader>
          <CardContent className='space-y-3 text-sm'>
            <div>
              <Label>PFX</Label>
              <Input type='file' accept='.pfx,.p12' onChange={(event) => setPfx(event.target.files?.[0] ?? null)} />
            </div>
            <div>
              <Label>PFX 密码</Label>
              <Input type='password' value={password} onChange={(event) => setPassword(event.target.value)} />
            </div>
            <div>
              <Label>或 PEM 证书</Label>
              <Input type='file' accept='.pem,.crt,.cer' onChange={(event) => setPem(event.target.files?.[0] ?? null)} />
            </div>
            <div>
              <Label>PEM 私钥</Label>
              <Input type='file' accept='.pem,.key' onChange={(event) => setKey(event.target.files?.[0] ?? null)} />
            </div>
            <Button variant='outline' onClick={() => void upload().catch((error: unknown) => setMessage(describeError(error)))}>
              导入
            </Button>
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>关闭</CardTitle>
          </CardHeader>
          <CardContent className='space-y-3 text-sm'>
            <p className='text-muted-foreground'>
              Docker 需要同时发布 HTTPS 端口。systemd 和 Windows 服务在重启后使用新绑定。未配置绑定文件时，进程仍按 HTTP 启动。
            </p>
            <Button variant='outline' onClick={() => void disable().catch((error: unknown) => setMessage(describeError(error)))}>
              关闭 HTTPS
            </Button>
          </CardContent>
        </Card>
      </div>
    </PageShell>
  )
}
