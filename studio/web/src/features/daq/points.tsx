import { useEffect, useState } from 'react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
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
  type PointDefinition,
  type PointSetDocument,
} from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'
import { PageShell } from './page-shell'

const blank = (): PointDefinition => ({
  id: '',
  address: '',
  dataType: 'string',
  unit: '',
  scale: 1,
  deadband: 0,
  enabled: true,
})

export function PointsPage() {
  const writable = canWrite(useAuthStore((state) => state.auth.user?.role[0]))
  const [devices, setDevices] = useState<DeviceDocument[]>([])
  const [deviceId, setDeviceId] = useState('')
  const [points, setPoints] = useState<PointDefinition[]>([])
  const [message, setMessage] = useState('')

  useEffect(() => {
    void studioApi<DeviceDocument[]>('/api/v1/config/devices').then((items) => {
      setDevices(items)
      if (items[0]) setDeviceId(items[0].metadata.id)
    })
  }, [])

  useEffect(() => {
    if (!deviceId) return
    void studioApi<PointSetDocument>(`/api/v1/config/points/${encodeURIComponent(deviceId)}`)
      .then((document) => setPoints(document.spec.points))
      .catch((error: Error) => setMessage(error.message))
  }, [deviceId])

  function update(index: number, patch: Partial<PointDefinition>) {
    setPoints((current) =>
      current.map((point, i) => (i === index ? { ...point, ...patch } : point))
    )
  }

  async function save() {
    const body: PointSetDocument = {
      metadata: { deviceId },
      spec: { points: points.filter((point) => point.id.trim()) },
    }
    await studioApi(`/api/v1/config/points/${encodeURIComponent(deviceId)}`, {
      method: 'PUT',
      body: JSON.stringify(body),
    })
    setPoints(body.spec.points)
    setMessage('点位已写入草稿')
    toast.success('点位已保存')
  }

  function exportCsv() {
    const lines = ['id,address,dataType,unit,enabled']
    for (const point of points) {
      lines.push(
        [point.id, point.address, point.dataType, point.unit, point.enabled].join(',')
      )
    }
    const blob = new Blob([lines.join('\n')], { type: 'text/csv' })
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = `${deviceId || 'points'}.csv`
    link.click()
    URL.revokeObjectURL(url)
  }

  async function importCsv(file: File) {
    const text = await file.text()
    const rows = text
      .split(/\r?\n/)
      .map((line) => line.trim())
      .filter(Boolean)
      .slice(1)
    setPoints(
      rows.map((line) => {
        const [id, address, dataType, unit, enabled] = line.split(',')
        return {
          ...blank(),
          id: id ?? '',
          address: address ?? '',
          dataType: dataType || 'string',
          unit: unit ?? '',
          enabled: enabled !== 'false',
        }
      })
    )
    setMessage('已从 CSV 填入表格，尚未保存')
  }

  return (
    <PageShell
      title='点位'
      description='按设备编辑点表。Excel 请另存为 CSV 再导入。保存的是整份 PointSet。'
      actions={
        <div className='flex gap-2'>
          <Button variant='outline' onClick={exportCsv} disabled={!deviceId}>
            导出 CSV
          </Button>
          <Button variant='outline' disabled={!writable} onClick={() => setPoints((current) => [...current, blank()])}>
            添加点位
          </Button>
        </div>
      }
    >
      <Card>
        <CardHeader className='flex flex-row items-center justify-between gap-3'>
          <CardTitle>点表</CardTitle>
          <div className='flex items-center gap-2'>
            <Select value={deviceId} onValueChange={setDeviceId}>
              <SelectTrigger className='w-56'>
                <SelectValue placeholder='选择设备' />
              </SelectTrigger>
              <SelectContent>
                {devices.map((device) => (
                  <SelectItem key={device.metadata.id} value={device.metadata.id}>
                    {device.metadata.displayName || device.metadata.id}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <Input
              type='file'
              accept='.csv,text/csv'
              disabled={!writable}
              className='max-w-48'
              onChange={(event) => {
                const file = event.target.files?.[0]
                if (file) void importCsv(file)
              }}
            />
          </div>
        </CardHeader>
        <CardContent>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>id</TableHead>
                <TableHead>address</TableHead>
                <TableHead>类型</TableHead>
                <TableHead>单位</TableHead>
                <TableHead>启用</TableHead>
                <TableHead />
              </TableRow>
            </TableHeader>
            <TableBody>
              {points.map((point, index) => (
                <TableRow key={`${point.id}-${index}`}>
                  <TableCell>
                    <Input value={point.id} disabled={!writable} onChange={(event) => update(index, { id: event.target.value })} />
                  </TableCell>
                  <TableCell>
                    <Input value={point.address} disabled={!writable} onChange={(event) => update(index, { address: event.target.value })} />
                  </TableCell>
                  <TableCell>
                    <Input value={point.dataType} disabled={!writable} onChange={(event) => update(index, { dataType: event.target.value })} />
                  </TableCell>
                  <TableCell>
                    <Input value={point.unit} disabled={!writable} onChange={(event) => update(index, { unit: event.target.value })} />
                  </TableCell>
                  <TableCell>
                    <Switch checked={point.enabled} disabled={!writable} onCheckedChange={(enabled) => update(index, { enabled })} />
                  </TableCell>
                  <TableCell>
                    <Button
                      size='sm'
                      variant='ghost'
                      disabled={!writable}
                      onClick={() => setPoints((current) => current.filter((_, i) => i !== index))}
                    >
                      移除
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <div className='mt-4 flex items-center gap-3'>
            <Button disabled={!writable || !deviceId} onClick={() => void save().catch((error: Error) => setMessage(error.message))}>
              保存草稿
            </Button>
            {message ? <p className='text-sm text-muted-foreground'>{message}</p> : null}
          </div>
        </CardContent>
      </Card>
    </PageShell>
  )
}
