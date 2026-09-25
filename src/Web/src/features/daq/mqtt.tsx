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
import {
  canWrite,
  describeError,
  expandTopic,
  studioApi,
  type ConfigView,
  type MqttSinkDocument,
} from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'
import { PageShell } from './page-shell'

export function MqttPage() {
  const writable = canWrite(useAuthStore((state) => state.auth.user?.role[0]))
  const [mqtt, setMqtt] = useState<MqttSinkDocument | null>(null)
  const [siteId, setSiteId] = useState('plant-a')
  const [sampleDevice, setSampleDevice] = useState('cnc-01')
  const [samplePoint, setSamplePoint] = useState('state')
  const [message, setMessage] = useState('')

  useEffect(() => {
    void studioApi<MqttSinkDocument>('/api/v1/config/sinks/mqtt')
      .then(setMqtt)
      .catch((error: unknown) => setMessage(describeError(error)))
    void studioApi<ConfigView>('/api/v1/config')
      .then((view) => {
        const site = view.draft.gateway.metadata.siteId
        if (site) setSiteId(site)
        const device = view.draft.devices?.[0]
        if (device?.metadata.id) setSampleDevice(device.metadata.id)
        const template = view.draft.pointTemplates?.find(
          (item) => item.metadata.id === device?.spec.pointTemplateId
        )
        const point = template?.spec.points.find((item) => item.enabled) ?? template?.spec.points[0]
        if (point?.id) setSamplePoint(point.id)
      })
      .catch(() => {
        // 预览仍用默认样例；Broker 表单自己的错误已经显示
      })
  }, [])

  async function save() {
    if (!mqtt) return
    const saved = await studioApi<MqttSinkDocument>('/api/v1/config/sinks/mqtt', {
      method: 'PUT',
      body: JSON.stringify(mqtt),
    })
    setMqtt(saved)
    setMessage('MQTT 已写入草稿。发布后采集才会连到这个 Broker。')
    toast.success('MQTT 已保存')
  }

  if (!mqtt) {
    return (
      <PageShell title='北向 MQTT'>
        <p className='text-sm text-muted-foreground'>{message || '加载中…'}</p>
      </PageShell>
    )
  }

  const broker = mqtt.spec.broker
  const pointTopic = expandTopic(mqtt.spec.topicTemplate, {
    site: siteId,
    deviceId: sampleDevice,
    point: samplePoint,
  })
  const statusTopic = expandTopic(mqtt.spec.statusTopic, {
    site: siteId,
    deviceId: sampleDevice,
  })
  const preview = {
    gatewayId: `gw-${siteId}`,
    site: siteId,
    deviceId: sampleDevice,
    point: samplePoint,
    value: 'RUNNING',
    quality: 'good',
    unit: null,
    ts: '2026-09-25T02:00:00.000Z',
  }

  return (
    <PageShell
      title='北向 MQTT'
      description='密码只写环境变量名，不写明文。主题按模板生成，默认 daq/{site}/{deviceId}/{point}。'
    >
      <div className='grid gap-4 xl:grid-cols-[minmax(0,1fr)_24rem]'>
        <Card>
          <CardHeader>
            <CardTitle>Broker</CardTitle>
          </CardHeader>
          <CardContent className='grid gap-3 sm:grid-cols-2'>
            <Field label='主机'>
              <Input
                value={broker.host}
                disabled={!writable}
                onChange={(event) => patchBroker(mqtt, setMqtt, { host: event.target.value })}
              />
            </Field>
            <Field label='端口'>
              <Input
                type='number'
                value={broker.port}
                disabled={!writable}
                onChange={(event) => patchBroker(mqtt, setMqtt, { port: Number(event.target.value) })}
              />
            </Field>
            <Field label='Client ID'>
              <Input
                value={broker.clientId}
                disabled={!writable}
                onChange={(event) => patchBroker(mqtt, setMqtt, { clientId: event.target.value })}
              />
            </Field>
            <Field label='QoS'>
              <Select
                value={String(mqtt.spec.qos)}
                disabled={!writable}
                onValueChange={(qos) => setMqtt({ ...mqtt, spec: { ...mqtt.spec, qos: Number(qos) } })}
              >
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value='0'>0 最多一次</SelectItem>
                  <SelectItem value='1'>1 至少一次</SelectItem>
                  <SelectItem value='2'>2 恰好一次</SelectItem>
                </SelectContent>
              </Select>
            </Field>
            <Field label='用户名环境变量'>
              <Input
                value={broker.usernameFromEnv ?? ''}
                disabled={!writable}
                placeholder='MQTT_USER'
                onChange={(event) =>
                  patchBroker(mqtt, setMqtt, { usernameFromEnv: event.target.value || null })
                }
              />
            </Field>
            <Field label='密码环境变量'>
              <Input
                value={broker.passwordFromEnv ?? ''}
                disabled={!writable}
                placeholder='MQTT_PASSWORD'
                onChange={(event) =>
                  patchBroker(mqtt, setMqtt, { passwordFromEnv: event.target.value || null })
                }
              />
            </Field>
            <p className='text-sm text-muted-foreground sm:col-span-2'>
              这里只保存环境变量名，不保存密码。Windows 服务从安装目录的 service.env 注入
              MQTT_USER 和 MQTT_PASSWORD。修改变量后要重新运行 install-service.bat 并重启服务。
            </p>
            <Field label='点位主题模板'>
              <Input
                value={mqtt.spec.topicTemplate}
                disabled={!writable}
                onChange={(event) =>
                  setMqtt({ ...mqtt, spec: { ...mqtt.spec, topicTemplate: event.target.value } })
                }
              />
            </Field>
            <Field label='状态主题模板'>
              <Input
                value={mqtt.spec.statusTopic}
                disabled={!writable}
                onChange={(event) =>
                  setMqtt({ ...mqtt, spec: { ...mqtt.spec, statusTopic: event.target.value } })
                }
              />
            </Field>
            <div className='flex items-center justify-between sm:col-span-1'>
              <Label>保留消息</Label>
              <Switch
                checked={mqtt.spec.retain}
                disabled={!writable}
                onCheckedChange={(retain) => setMqtt({ ...mqtt, spec: { ...mqtt.spec, retain } })}
              />
            </div>
            <div className='flex items-center justify-between sm:col-span-1'>
              <Label>TLS</Label>
              <Switch
                checked={Boolean(broker.tls)}
                disabled={!writable}
                onCheckedChange={(tls) => patchBroker(mqtt, setMqtt, { tls })}
              />
            </div>
            <div className='sm:col-span-2'>
              <Button
                disabled={!writable}
                onClick={() =>
                  void save().catch((error: unknown) => {
                    const text = describeError(error)
                    setMessage(text)
                    toast.error(text)
                  })
                }
              >
                保存草稿
              </Button>
              {message ? <p className='mt-2 text-sm text-muted-foreground'>{message}</p> : null}
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>预览</CardTitle>
          </CardHeader>
          <CardContent className='space-y-3 text-sm'>
            <p className='text-muted-foreground'>
              只在浏览器里用当前模板和一条样例点拼出来，不连接 Broker。样例站点 {siteId}，设备 {sampleDevice}。
            </p>
            <div>
              <div className='text-muted-foreground'>点位主题</div>
              <div className='font-mono text-xs break-all'>{pointTopic}</div>
            </div>
            <div>
              <div className='text-muted-foreground'>状态主题</div>
              <div className='font-mono text-xs break-all'>{statusTopic}</div>
            </div>
            <div>
              <div className='mb-1 text-muted-foreground'>JSON 字段</div>
              <pre className='overflow-auto rounded-md bg-muted p-3 text-xs'>{JSON.stringify(preview, null, 2)}</pre>
            </div>
          </CardContent>
        </Card>
      </div>
    </PageShell>
  )
}

function patchBroker(
  mqtt: MqttSinkDocument,
  setMqtt: (next: MqttSinkDocument) => void,
  patch: Partial<MqttSinkDocument['spec']['broker']>
) {
  setMqtt({
    ...mqtt,
    spec: { ...mqtt.spec, broker: { ...mqtt.spec.broker, ...patch } },
  })
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className='grid gap-1.5'>
      <Label>{label}</Label>
      {children}
    </div>
  )
}
