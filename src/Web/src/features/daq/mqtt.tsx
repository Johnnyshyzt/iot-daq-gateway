import { useEffect, useState } from 'react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { canWrite, studioApi, type MqttSinkDocument } from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'
import { PageShell } from './page-shell'

export function MqttPage() {
  const writable = canWrite(useAuthStore((state) => state.auth.user?.role[0]))
  const [mqtt, setMqtt] = useState<MqttSinkDocument | null>(null)
  const [message, setMessage] = useState('')

  useEffect(() => {
    void studioApi<MqttSinkDocument>('/api/v1/config/sinks/mqtt')
      .then(setMqtt)
      .catch((error: Error) => setMessage(error.message))
  }, [])

  async function save() {
    if (!mqtt) return
    const saved = await studioApi<MqttSinkDocument>('/api/v1/config/sinks/mqtt', {
      method: 'PUT',
      body: JSON.stringify(mqtt),
    })
    setMqtt(saved)
    setMessage('MQTT 已写入草稿')
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
  return (
    <PageShell
      title='北向 MQTT'
      description='密码只写环境变量名，不写明文。主题仍是 daq/{site}/{deviceId}/{point}。'
    >
      <Card className='max-w-2xl'>
        <CardHeader>
          <CardTitle>Broker</CardTitle>
        </CardHeader>
        <CardContent className='grid gap-3 sm:grid-cols-2'>
          <Field label='主机'>
            <Input
              value={broker.host}
              disabled={!writable}
              onChange={(event) =>
                setMqtt({ ...mqtt, spec: { ...mqtt.spec, broker: { ...broker, host: event.target.value } } })
              }
            />
          </Field>
          <Field label='端口'>
            <Input
              type='number'
              value={broker.port}
              disabled={!writable}
              onChange={(event) =>
                setMqtt({
                  ...mqtt,
                  spec: { ...mqtt.spec, broker: { ...broker, port: Number(event.target.value) } },
                })
              }
            />
          </Field>
          <Field label='Client ID'>
            <Input
              value={broker.clientId}
              disabled={!writable}
              onChange={(event) =>
                setMqtt({
                  ...mqtt,
                  spec: { ...mqtt.spec, broker: { ...broker, clientId: event.target.value } },
                })
              }
            />
          </Field>
          <Field label='QoS'>
            <Input
              type='number'
              value={mqtt.spec.qos}
              disabled={!writable}
              onChange={(event) =>
                setMqtt({ ...mqtt, spec: { ...mqtt.spec, qos: Number(event.target.value) } })
              }
            />
          </Field>
          <Field label='用户名环境变量'>
            <Input
              value={broker.usernameFromEnv ?? ''}
              disabled={!writable}
              placeholder='MQTT_USER'
              onChange={(event) =>
                setMqtt({
                  ...mqtt,
                  spec: {
                    ...mqtt.spec,
                    broker: { ...broker, usernameFromEnv: event.target.value || null },
                  },
                })
              }
            />
          </Field>
          <Field label='密码环境变量'>
            <Input
              value={broker.passwordFromEnv ?? ''}
              disabled={!writable}
              placeholder='MQTT_PASSWORD'
              onChange={(event) =>
                setMqtt({
                  ...mqtt,
                  spec: {
                    ...mqtt.spec,
                    broker: { ...broker, passwordFromEnv: event.target.value || null },
                  },
                })
              }
            />
          </Field>
          <Field label='点位主题'>
            <Input
              value={mqtt.spec.topicTemplate}
              disabled={!writable}
              onChange={(event) =>
                setMqtt({ ...mqtt, spec: { ...mqtt.spec, topicTemplate: event.target.value } })
              }
            />
          </Field>
          <Field label='状态主题'>
            <Input
              value={mqtt.spec.statusTopic}
              disabled={!writable}
              onChange={(event) =>
                setMqtt({ ...mqtt, spec: { ...mqtt.spec, statusTopic: event.target.value } })
              }
            />
          </Field>
          <div className='sm:col-span-2'>
            <Button disabled={!writable} onClick={() => void save().catch((error: Error) => setMessage(error.message))}>
              保存草稿
            </Button>
            {message ? <p className='mt-2 text-sm text-muted-foreground'>{message}</p> : null}
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
