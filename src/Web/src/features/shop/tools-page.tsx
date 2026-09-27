import { useEffect, useMemo, useState } from 'react'
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
import { PageShell } from '@/features/daq/page-shell'
import { downloadFile, formatTime } from '@/features/viz/format'
import {
  canOperate,
  canWrite,
  describeError,
  studioApi,
  type DeviceDocument,
} from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'

type Tool = {
  id: string
  toolNumber: string
  description: string
  lifeLimitCount: number | null
  lifeLimitMinutes: number | null
  warningPercent: number
  enabled: boolean
}

type Life = {
  deviceId: string
  toolNumber: string
  usedCount: number
  usedCuttingMs: number
  source: string
  level: string
  updatedUnixMs: number
}

type Pocket = { id: string; deviceId: string; pocket: number; toolNumber: string }
type Change = {
  deviceId: string
  pocket: number | null
  oldToolNumber: string
  newToolNumber: string
  note: string
  actor: string
  unixMs: number
  resetLife: boolean
}

type Brand = { brandId: string; nameZh: string; counting: string }

type ToolsView = {
  licensed: boolean
  message?: string
  tools: Tool[]
  pockets: Pocket[]
  life: Life[]
  changes: Change[]
  brands: Brand[]
}

const levelText: Record<string, string> = { ok: '正常', warning: '预警', eol: '到寿' }

function levelBadge(level: string) {
  if (level === 'eol') return <Badge variant='destructive'>到寿</Badge>
  if (level === 'warning') return <Badge variant='secondary'>预警</Badge>
  return <Badge variant='outline'>正常</Badge>
}

export function ToolsPage() {
  const role = useAuthStore((state) => state.auth.user?.role[0])
  const writable = canWrite(role)
  const operate = canOperate(role)
  const [view, setView] = useState<ToolsView | null>(null)
  const [devices, setDevices] = useState<DeviceDocument[]>([])
  const [error, setError] = useState('')
  const [toolNumber, setToolNumber] = useState('')
  const [description, setDescription] = useState('')
  const [limitCount, setLimitCount] = useState('')
  const [limitMinutes, setLimitMinutes] = useState('')
  const [warning, setWarning] = useState('80')
  const [deviceId, setDeviceId] = useState('')
  const [pocket, setPocket] = useState('1')
  const [oldTool, setOldTool] = useState('')
  const [newTool, setNewTool] = useState('')
  const [note, setNote] = useState('')
  const [resetLife, setResetLife] = useState(true)
  const [countTool, setCountTool] = useState('')
  const [count, setCount] = useState('1')

  async function load() {
    const [body, list] = await Promise.all([
      studioApi<ToolsView>('/api/v1/tools'),
      studioApi<DeviceDocument[]>('/api/v1/config/devices'),
    ])
    setView(body)
    setDevices(list)
    if (!deviceId && list[0]) setDeviceId(list[0].metadata.id)
  }

  useEffect(() => {
    void load().catch((err: unknown) => setError(describeError(err)))
  }, [])

  const rows = useMemo(() => {
    const tools = view?.tools ?? []
    const life = view?.life ?? []
    const used = new Set<string>()
    const merged: { life?: Life; master?: Tool }[] = life.map((item) => {
      const master = tools.find((tool) => tool.toolNumber === item.toolNumber)
      used.add(item.toolNumber)
      return { life: item, master }
    })
    for (const tool of tools) {
      if (!used.has(tool.toolNumber)) merged.push({ life: undefined, master: tool })
    }
    return merged
  }, [view])

  async function saveTool() {
    const id = `tool-${toolNumber.trim().toLowerCase().replace(/[^a-z0-9_-]/g, '') || crypto.randomUUID().replace(/-/g, '')}`
    await studioApi(`/api/v1/tools/${encodeURIComponent(id)}`, {
      method: 'PUT',
      body: JSON.stringify({
        toolNumber,
        description,
        lifeLimitCount: limitCount ? Number(limitCount) : null,
        lifeLimitMinutes: limitMinutes ? Number(limitMinutes) : null,
        warningPercent: Number(warning) || 80,
        enabled: true,
      }),
    })
    setToolNumber('')
    setDescription('')
    await load()
  }

  async function recordChange() {
    await studioApi('/api/v1/tools/change', {
      method: 'POST',
      body: JSON.stringify({
        deviceId,
        pocket: Number(pocket) || null,
        oldToolNumber: oldTool,
        newToolNumber: newTool,
        note,
        resetLife,
      }),
    })
    setNote('')
    await load()
  }

  async function addCount() {
    await studioApi('/api/v1/tools/count', {
      method: 'POST',
      body: JSON.stringify({ deviceId, toolNumber: countTool, count: Number(count) || 1 }),
    })
    await load()
  }

  return (
    <PageShell
      title='刀具寿命'
      description='按刀号累计件数和切削时间。驱动没有刀号时，用手动计数或计算点写出 toolNumber / partCount。预警和到寿进入现有报警与通知。'
      actions={
        <Button variant='outline' onClick={() => void downloadFile('/api/v1/tools/report', 'tools.csv').catch((err: unknown) => setError(describeError(err)))}>
          导出 CSV
        </Button>
      }
    >
      {error ? <p className='mb-3 text-sm text-destructive'>{error}</p> : null}
      {view && !view.licensed ? <p className='text-sm text-muted-foreground'>{view.message || '当前许可证不包含刀具寿命。'}</p> : null}
      {view?.licensed ? (
        <div className='grid gap-4'>
          <Card>
            <CardHeader><CardTitle className='text-base'>寿命</CardTitle></CardHeader>
            <CardContent>
              <Table data-testid='tool-life-list'>
                <TableHeader>
                  <TableRow>
                    <TableHead>刀号</TableHead>
                    <TableHead>说明</TableHead>
                    <TableHead>设备</TableHead>
                    <TableHead>件数</TableHead>
                    <TableHead>切削分钟</TableHead>
                    <TableHead>状态</TableHead>
                    <TableHead>来源</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {rows.map((row) => {
                    const number = row.life?.toolNumber ?? row.master?.toolNumber ?? ''
                    const limitCount = row.master?.lifeLimitCount
                    const limitMinutes = row.master?.lifeLimitMinutes
                    return (
                      <TableRow key={`${row.life?.deviceId ?? 'master'}-${number}`}>
                        <TableCell>{number}</TableCell>
                        <TableCell>{row.master?.description || '—'}</TableCell>
                        <TableCell>{row.life?.deviceId || '—'}</TableCell>
                        <TableCell>
                          {row.life ? row.life.usedCount : 0}
                          {limitCount ? ` / ${limitCount}` : ''}
                        </TableCell>
                        <TableCell>
                          {((row.life?.usedCuttingMs ?? 0) / 60000).toFixed(1)}
                          {limitMinutes ? ` / ${limitMinutes}` : ''}
                        </TableCell>
                        <TableCell>{levelBadge(row.life?.level ?? 'ok')}</TableCell>
                        <TableCell>{row.life?.source || '—'}</TableCell>
                      </TableRow>
                    )
                  })}
                </TableBody>
              </Table>
            </CardContent>
          </Card>

          <div className='grid gap-4 lg:grid-cols-2'>
            <Card>
              <CardHeader><CardTitle className='text-base'>刀具主数据</CardTitle></CardHeader>
              <CardContent className='grid gap-3'>
                <p className='text-sm text-muted-foreground'>工程师维护刀号、件数上限、切削时间上限和预警百分比。</p>
                <div className='grid gap-2 sm:grid-cols-2'>
                  <div><Label>刀号</Label><Input value={toolNumber} onChange={(event) => setToolNumber(event.target.value)} /></div>
                  <div><Label>说明</Label><Input value={description} onChange={(event) => setDescription(event.target.value)} /></div>
                  <div><Label>件数上限</Label><Input value={limitCount} onChange={(event) => setLimitCount(event.target.value)} /></div>
                  <div><Label>切削分钟上限</Label><Input value={limitMinutes} onChange={(event) => setLimitMinutes(event.target.value)} /></div>
                  <div><Label>预警 %</Label><Input value={warning} onChange={(event) => setWarning(event.target.value)} /></div>
                </div>
                <Button disabled={!writable} onClick={() => void saveTool().catch((err: unknown) => setError(describeError(err)))}>保存刀具</Button>
              </CardContent>
            </Card>

            <Card>
              <CardHeader><CardTitle className='text-base'>换刀记录</CardTitle></CardHeader>
              <CardContent className='grid gap-3' data-testid='tool-change-form'>
                <p className='text-sm text-muted-foreground'>操作员记录换刀。勾选重置后，该刀在这台设备上的累计件数和切削时间清零，预警报警关闭。</p>
                {deviceId ? (
                  <Select value={deviceId} onValueChange={setDeviceId}>
                    <SelectTrigger><SelectValue placeholder='设备' /></SelectTrigger>
                    <SelectContent>
                      {devices.map((device) => (
                        <SelectItem key={device.metadata.id} value={device.metadata.id}>{device.metadata.displayName}</SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                ) : <p className='text-sm text-muted-foreground'>还没有设备。</p>}
                <div className='grid gap-2 sm:grid-cols-3'>
                  <div><Label>刀位</Label><Input value={pocket} onChange={(event) => setPocket(event.target.value)} /></div>
                  <div><Label>旧刀</Label><Input value={oldTool} onChange={(event) => setOldTool(event.target.value)} /></div>
                  <div><Label>新刀</Label><Input value={newTool} onChange={(event) => setNewTool(event.target.value)} /></div>
                </div>
                <div><Label>备注</Label><Input value={note} onChange={(event) => setNote(event.target.value)} /></div>
                <label className='flex items-center gap-2 text-sm'>
                  <input type='checkbox' checked={resetLife} onChange={(event) => setResetLife(event.target.checked)} />
                  重置寿命
                </label>
                <Button disabled={!operate} onClick={() => void recordChange().catch((err: unknown) => setError(describeError(err)))}>记录换刀</Button>
                <div className='grid gap-2 sm:grid-cols-[1fr_6rem_auto]'>
                  <Input placeholder='手动计数刀号' value={countTool} onChange={(event) => setCountTool(event.target.value)} />
                  <Input value={count} onChange={(event) => setCount(event.target.value)} />
                  <Button variant='outline' disabled={!operate} onClick={() => void addCount().catch((err: unknown) => setError(describeError(err)))}>加件数</Button>
                </div>
              </CardContent>
            </Card>
          </div>

          <div className='grid gap-4 lg:grid-cols-2'>
            <Card>
              <CardHeader><CardTitle className='text-base'>刀库刀位</CardTitle></CardHeader>
              <CardContent>
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>设备</TableHead>
                      <TableHead>刀位</TableHead>
                      <TableHead>刀号</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {(view.pockets ?? []).map((item) => (
                      <TableRow key={item.id}>
                        <TableCell>{item.deviceId}</TableCell>
                        <TableCell>{item.pocket}</TableCell>
                        <TableCell>{item.toolNumber}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </CardContent>
            </Card>
            <Card>
              <CardHeader><CardTitle className='text-base'>换刀历史</CardTitle></CardHeader>
              <CardContent>
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>时间</TableHead>
                      <TableHead>设备</TableHead>
                      <TableHead>更换</TableHead>
                      <TableHead>重置</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {(view.changes ?? []).map((item, index) => (
                      <TableRow key={`${item.unixMs}-${index}`}>
                        <TableCell>{formatTime(item.unixMs)}</TableCell>
                        <TableCell>{item.deviceId}{item.pocket ? ` #${item.pocket}` : ''}</TableCell>
                        <TableCell>{item.oldToolNumber || '—'} → {item.newToolNumber || '—'}</TableCell>
                        <TableCell>{item.resetLife ? '是' : '否'}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </CardContent>
            </Card>
          </div>

          <Card>
            <CardHeader><CardTitle className='text-base'>品牌计数能力</CardTitle></CardHeader>
            <CardContent className='grid gap-2 text-sm'>
              {(view.brands ?? []).slice(0, 8).map((brand) => (
                <p key={brand.brandId}><span className='font-medium'>{brand.nameZh}</span> {brand.counting}</p>
              ))}
              <p className='text-muted-foreground'>状态列：{Object.values(levelText).join('、')}。没有刀号的品牌不会凭空读刀具数据。</p>
            </CardContent>
          </Card>
        </div>
      ) : null}
    </PageShell>
  )
}
