import { useEffect, useState } from 'react'
import { toast } from 'sonner'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
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
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import {
  canWrite,
  studioApi,
  type DeviceDocument,
} from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'
import { PageShell } from './page-shell'

const emptyDevice = (): DeviceDocument => ({
  metadata: { id: '', displayName: '' },
  spec: {
    adapter: 'fanuc.fake',
    enabled: true,
    intervalMs: 1000,
    connection: { host: '127.0.0.1', port: 8193, focasTimeoutMs: 3000 },
  },
})

export function DevicesPage() {
  const role = useAuthStore((state) => state.auth.user?.role[0])
  const writable = canWrite(role)
  const [devices, setDevices] = useState<DeviceDocument[]>([])
  const [draft, setDraft] = useState<DeviceDocument>(emptyDevice())
  const [message, setMessage] = useState('')

  async function reload() {
    setDevices(await studioApi<DeviceDocument[]>('/api/v1/config/devices'))
  }

  useEffect(() => {
    void reload().catch((error: Error) => setMessage(error.message))
  }, [])

  function select(device: DeviceDocument) {
    setDraft(structuredClone(device))
    setMessage('')
  }

  async function save() {
    const id = draft.metadata.id.trim()
    if (!id) {
      setMessage('设备 id 不能为空')
      return
    }
    const body: DeviceDocument = {
      ...draft,
      metadata: { ...draft.metadata, id },
    }
    await studioApi(`/api/v1/config/devices/${encodeURIComponent(id)}`, {
      method: 'PUT',
      body: JSON.stringify(body),
    })
    await reload()
    setDraft(body)
    setMessage('已写入草稿')
    toast.success('设备已保存到草稿')
  }

  async function remove(id: string) {
    await studioApi(`/api/v1/config/devices/${encodeURIComponent(id)}`, {
      method: 'DELETE',
    })
    if (draft.metadata.id === id) setDraft(emptyDevice())
    await reload()
    toast.success('设备已从草稿删除')
  }

  async function test(id: string) {
    const result = await studioApi<{ ok: boolean; message: string }>(
      `/api/v1/devices/${encodeURIComponent(id)}/test`,
      { method: 'POST' }
    )
    setMessage(result.message)
    toast[result.ok ? 'success' : 'error'](result.message)
  }

  return (
    <PageShell
      title='设备'
      description='Fanuc 设备只支持 fanuc.fake 与 fanuc.focas。保存写入草稿，发布后网关才会加载。'
      actions={
        <Button
          variant='outline'
          disabled={!writable}
          onClick={() => setDraft(emptyDevice())}
        >
          新建设备
        </Button>
      }
    >
      <div className='grid gap-4 lg:grid-cols-[minmax(0,1fr)_22rem]'>
        <Card>
          <CardHeader>
            <CardTitle>设备列表</CardTitle>
          </CardHeader>
          <CardContent>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>名称</TableHead>
                  <TableHead>适配器</TableHead>
                  <TableHead>状态</TableHead>
                  <TableHead />
                </TableRow>
              </TableHeader>
              <TableBody>
                {devices.map((device) => (
                  <TableRow key={device.metadata.id}>
                    <TableCell>
                      <div className='font-medium'>{device.metadata.displayName}</div>
                      <div className='text-xs text-muted-foreground'>{device.metadata.id}</div>
                    </TableCell>
                    <TableCell>{device.spec.adapter}</TableCell>
                    <TableCell>
                      <Badge variant={device.spec.enabled ? 'default' : 'secondary'}>
                        {device.spec.enabled ? '启用' : '禁用'}
                      </Badge>
                    </TableCell>
                    <TableCell className='space-x-2 text-end'>
                      <Button size='sm' variant='outline' onClick={() => select(device)}>
                        编辑
                      </Button>
                      <Button
                        size='sm'
                        variant='outline'
                        disabled={!writable}
                        onClick={() => void test(device.metadata.id)}
                      >
                        测试
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>{draft.metadata.id ? '编辑设备' : '新建设备'}</CardTitle>
          </CardHeader>
          <CardContent className='grid gap-3'>
            <Field label='设备 id'>
              <Input
                value={draft.metadata.id}
                disabled={!writable}
                onChange={(event) =>
                  setDraft({
                    ...draft,
                    metadata: { ...draft.metadata, id: event.target.value },
                  })
                }
              />
            </Field>
            <Field label='显示名'>
              <Input
                value={draft.metadata.displayName}
                disabled={!writable}
                onChange={(event) =>
                  setDraft({
                    ...draft,
                    metadata: { ...draft.metadata, displayName: event.target.value },
                  })
                }
              />
            </Field>
            <Field label='适配器'>
              <Select
                value={draft.spec.adapter}
                disabled={!writable}
                onValueChange={(adapter) =>
                  setDraft({ ...draft, spec: { ...draft.spec, adapter } })
                }
              >
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value='fanuc.fake'>fanuc.fake（模拟）</SelectItem>
                  <SelectItem value='fanuc.focas'>fanuc.focas</SelectItem>
                </SelectContent>
              </Select>
            </Field>
            <Field label='主机'>
              <Input
                value={draft.spec.connection.host}
                disabled={!writable}
                onChange={(event) =>
                  setDraft({
                    ...draft,
                    spec: {
                      ...draft.spec,
                      connection: { ...draft.spec.connection, host: event.target.value },
                    },
                  })
                }
              />
            </Field>
            <div className='grid grid-cols-2 gap-3'>
              <Field label='端口'>
                <Input
                  type='number'
                  value={draft.spec.connection.port}
                  disabled={!writable}
                  onChange={(event) =>
                    setDraft({
                      ...draft,
                      spec: {
                        ...draft.spec,
                        connection: {
                          ...draft.spec.connection,
                          port: Number(event.target.value),
                        },
                      },
                    })
                  }
                />
              </Field>
              <Field label='扫描周期 ms'>
                <Input
                  type='number'
                  value={draft.spec.intervalMs}
                  disabled={!writable}
                  onChange={(event) =>
                    setDraft({
                      ...draft,
                      spec: { ...draft.spec, intervalMs: Number(event.target.value) },
                    })
                  }
                />
              </Field>
            </div>
            <div className='flex items-center justify-between'>
              <Label>启用</Label>
              <Switch
                checked={draft.spec.enabled}
                disabled={!writable}
                onCheckedChange={(enabled) =>
                  setDraft({ ...draft, spec: { ...draft.spec, enabled } })
                }
              />
            </div>
            <div className='flex gap-2'>
              <Button disabled={!writable} onClick={() => void save().catch((error: Error) => setMessage(error.message))}>
                保存草稿
              </Button>
              <Button
                variant='destructive'
                disabled={!writable || !draft.metadata.id}
                onClick={() =>
                  void remove(draft.metadata.id).catch((error: Error) => setMessage(error.message))
                }
              >
                删除
              </Button>
            </div>
            {message ? <p className='text-sm text-muted-foreground'>{message}</p> : null}
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
