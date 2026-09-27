import { useEffect, useState } from 'react'
import { toast } from 'sonner'
import { Badge } from '@/components/ui/badge'
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
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { canWrite, describeError, studioApi, studioDownload, type DeviceDocument } from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'
import { PageShell } from './page-shell'

type Stage = {
  id: string
  title: string
  status: string
  detail: string
  hint: string
  elapsedMs: number
}

type SelfTestReport = {
  deviceId: string
  adapter: string
  protocol: string
  passed: boolean
  summary: string
  hint: string
  stages: Stage[]
}

type TraceLine = { t: number; dir: string; hex: string; text: string }

type TraceView = {
  deviceId: string
  active: boolean
  reason: string
  bytes: number
  maxBytes: number
  lines: TraceLine[]
}

const statusText: Record<string, string> = {
  pass: '通过',
  fail: '失败',
  skip: '跳过',
}

export function CommissionPage() {
  const writable = canWrite(useAuthStore((state) => state.auth.user?.role[0]))
  const [devices, setDevices] = useState<DeviceDocument[]>([])
  const [deviceId, setDeviceId] = useState('')
  const [report, setReport] = useState<SelfTestReport | null>(null)
  const [trace, setTrace] = useState<TraceView | null>(null)
  const [seconds, setSeconds] = useState(60)
  const [checklist, setChecklist] = useState('')
  const [message, setMessage] = useState('')
  const [busy, setBusy] = useState('')

  useEffect(() => {
    void studioApi<DeviceDocument[]>('/api/v1/config/devices')
      .then((items) => {
        setDevices(items)
        setDeviceId((current) => current || items[0]?.metadata.id || '')
      })
      .catch((error: unknown) => setMessage(describeError(error)))
    void studioApi<{ markdown: string }>('/api/v1/ops/checklist')
      .then((body) => setChecklist(body.markdown))
      .catch(() => setChecklist(''))
  }, [])

  useEffect(() => {
    if (!deviceId) return
    void studioApi<SelfTestReport>(`/api/v1/devices/${encodeURIComponent(deviceId)}/self-test`)
      .then(setReport)
      .catch(() => setReport(null))
    void refreshTrace(deviceId)
  }, [deviceId])

  useEffect(() => {
    if (!trace?.active || !deviceId) return
    const timer = window.setInterval(() => void refreshTrace(deviceId), 2000)
    return () => window.clearInterval(timer)
  }, [trace?.active, deviceId])

  async function refreshTrace(id: string) {
    const view = await studioApi<TraceView>(`/api/v1/devices/${encodeURIComponent(id)}/trace`)
    setTrace(view)
  }

  async function runTest() {
    setBusy('test')
    setMessage('')
    try {
      const next = await studioApi<SelfTestReport>(`/api/v1/devices/${encodeURIComponent(deviceId)}/self-test`, {
        method: 'POST',
        body: '{}',
      })
      setReport(next)
      toast.success(next.passed ? '自检通过' : '自检未通过')
    } catch (error) {
      setMessage(describeError(error))
    } finally {
      setBusy('')
    }
  }

  async function startTrace() {
    setBusy('trace')
    setMessage('')
    try {
      await studioApi(`/api/v1/devices/${encodeURIComponent(deviceId)}/trace`, {
        method: 'POST',
        body: JSON.stringify({ seconds, maxBytes: 1_000_000 }),
      })
      await refreshTrace(deviceId)
      toast.success('已开始记录报文')
    } catch (error) {
      setMessage(describeError(error))
    } finally {
      setBusy('')
    }
  }

  async function stopTrace() {
    setBusy('trace')
    try {
      await studioApi(`/api/v1/devices/${encodeURIComponent(deviceId)}/trace/stop`, { method: 'POST' })
      await refreshTrace(deviceId)
    } catch (error) {
      setMessage(describeError(error))
    } finally {
      setBusy('')
    }
  }

  async function exportBundle() {
    setBusy('bundle')
    setMessage('')
    try {
      const stamp = new Date().toISOString().replace(/[:.]/g, '-')
      await studioDownload('/api/v1/ops/diagnose', `iot-daq-gateway-diagnose-${stamp}.zip`, { method: 'POST' })
      toast.success('诊断包已下载')
    } catch (error) {
      setMessage(describeError(error))
    } finally {
      setBusy('')
    }
  }

  return (
    <PageShell
      title='现场调试'
      description='连通性自检、报文记录和诊断包。没有真实机床时，模拟器会走完各阶段并说明原因。'
    >
      {message ? <p className='mb-3 text-sm text-destructive'>{message}</p> : null}
      <div className='mb-4 flex max-w-md flex-col gap-2'>
        <Label>设备</Label>
        <Select value={deviceId} onValueChange={setDeviceId}>
          <SelectTrigger data-testid='commission-device'>
            <SelectValue placeholder={devices.length === 0 ? '还没有设备' : '选择设备'} />
          </SelectTrigger>
          <SelectContent>
            {devices.map((device) => (
              <SelectItem key={device.metadata.id} value={device.metadata.id}>
                {device.metadata.displayName || device.metadata.id} · {device.spec.adapter}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <div className='grid gap-4 lg:grid-cols-2'>
        <Card data-testid='self-test-result'>
          <CardHeader>
            <CardTitle className='flex items-center gap-2'>
              连通性自检
              {report ? (
                <Badge variant={report.passed ? 'default' : 'destructive'}>{report.passed ? '通过' : '未通过'}</Badge>
              ) : null}
            </CardTitle>
          </CardHeader>
          <CardContent className='space-y-3 text-sm'>
            <p className='text-muted-foreground'>
              依次检查域名、TCP 端口、协议握手、样例点和耗时。提示按协议给出，不调用未安装的厂商 SDK。
            </p>
            <Button disabled={!writable || !deviceId || busy === 'test'} onClick={() => void runTest()}>
              {busy === 'test' ? '正在自检…' : '开始自检'}
            </Button>
            {!writable ? <p className='text-muted-foreground'>操作员和只读账号可以查看结果，不能发起自检。</p> : null}
            {report ? (
              <>
                <p>
                  {report.summary}
                  {report.protocol ? ` · ${report.protocol}` : ''}
                </p>
                {report.hint ? <p>{report.hint}</p> : null}
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>阶段</TableHead>
                      <TableHead>结果</TableHead>
                      <TableHead>说明</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {report.stages.map((stage) => (
                      <TableRow key={stage.id}>
                        <TableCell>{stage.title}</TableCell>
                        <TableCell>{statusText[stage.status] ?? stage.status}</TableCell>
                        <TableCell>
                          <div>{stage.detail}</div>
                          {stage.hint ? <div className='mt-1 text-muted-foreground'>{stage.hint}</div> : null}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </>
            ) : (
              <p className='text-muted-foreground'>这台设备还没有自检记录。</p>
            )}
          </CardContent>
        </Card>
        <Card data-testid='trace-viewer'>
          <CardHeader>
            <CardTitle>报文记录</CardTitle>
          </CardHeader>
          <CardContent className='space-y-3 text-sm'>
            <p className='text-muted-foreground'>
              限时写入环形文件。口令会打码。到时或达到大小上限后自动停止。Modbus、LSV2、Haas Q 会留下十六进制和解析文本。
            </p>
            <div className='flex items-end gap-2'>
              <div>
                <Label>秒数</Label>
                <Input
                  type='number'
                  min={5}
                  max={900}
                  value={seconds}
                  onChange={(event) => setSeconds(Number(event.target.value))}
                  className='w-24'
                />
              </div>
              <Button disabled={!writable || !deviceId || busy === 'trace'} onClick={() => void startTrace()}>
                开始记录
              </Button>
              <Button variant='outline' disabled={!writable || !deviceId} onClick={() => void stopTrace()}>
                停止
              </Button>
              <Button
                variant='outline'
                disabled={!deviceId}
                onClick={() =>
                  void studioDownload(`/api/v1/devices/${encodeURIComponent(deviceId)}/trace/download`, `${deviceId}.jsonl`).catch(
                    (error: unknown) => setMessage(describeError(error))
                  )
                }
              >
                下载
              </Button>
            </div>
            {trace ? (
              <p>
                {trace.active ? '记录中' : '已停止'}
                {trace.reason ? ` · ${trace.reason}` : ''} · {trace.bytes} / {trace.maxBytes || '—'} 字节 · {trace.lines.length} 行
              </p>
            ) : null}
            <div className='max-h-72 overflow-auto rounded-md border font-mono text-xs'>
              {(trace?.lines ?? []).length === 0 ? (
                <p className='p-3 text-muted-foreground'>还没有报文。自检或采集时，已支持的驱动会写入请求和响应。</p>
              ) : (
                <Table>
                  <TableBody>
                    {trace?.lines.map((line, index) => (
                      <TableRow key={`${line.t}-${index}`}>
                        <TableCell className='whitespace-nowrap'>{line.dir}</TableCell>
                        <TableCell className='break-all'>{line.text || line.hex}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}
            </div>
          </CardContent>
        </Card>
        <Card data-testid='diagnostic-export'>
          <CardHeader>
            <CardTitle>诊断包</CardTitle>
          </CardHeader>
          <CardContent className='space-y-3 text-sm'>
            <p className='text-muted-foreground'>
              一个 zip：版本、许可证摘要、打码后的配置、最近日志、链路状态、缓冲大小、自检、报文和系统信息。不含私钥、证书和账号文件。命令行可用 --diagnose。
            </p>
            <Button disabled={!writable || busy === 'bundle'} onClick={() => void exportBundle()}>
              {busy === 'bundle' ? '正在打包…' : '导出诊断包'}
            </Button>
          </CardContent>
        </Card>
        <Card data-testid='field-checklist'>
          <CardHeader>
            <CardTitle>现场调试清单</CardTitle>
          </CardHeader>
          <CardContent>
            <pre className='max-h-96 overflow-auto whitespace-pre-wrap text-sm'>{checklist || '正在读取清单…'}</pre>
          </CardContent>
        </Card>
      </div>
    </PageShell>
  )
}
