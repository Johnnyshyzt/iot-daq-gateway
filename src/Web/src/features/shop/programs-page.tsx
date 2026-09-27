import { useEffect, useState } from 'react'
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
import { Textarea } from '@/components/ui/textarea'
import { PageShell } from '@/features/daq/page-shell'
import { formatTime } from '@/features/viz/format'
import {
  canWrite,
  describeError,
  isAdmin,
  studioApi,
  type DeviceDocument,
} from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'

type Version = {
  id: string
  version: number
  checksum: string
  comment: string
  status: string
  uploadedBy: string
  uploadedUnixMs: number
  approvedBy?: string | null
}

type Program = {
  id: string
  name: string
  comment: string
  status: string
  versions: Version[]
  devices: { deviceId: string }[]
}

type Channel = { channel: string; adapterId: string; canTransfer: boolean; note: string }
type Brand = { brandId: string; nameZh: string; channels: Channel[] }
type DiffLine = { kind: string; text: string }
type Transfer = { programId: string; deviceId: string; direction: string; channel: string; checksum: string; result: string; message: string; unixMs: number }

type ProgramsView = {
  licensed: boolean
  message?: string
  programs: Program[]
  brands: Brand[]
  transfers?: Transfer[]
}

const statusText: Record<string, string> = { draft: '草稿', approved: '已批准', archived: '已归档' }

export function ProgramsPage() {
  const role = useAuthStore((state) => state.auth.user?.role[0])
  const writable = canWrite(role)
  const admin = isAdmin(role)
  const [view, setView] = useState<ProgramsView | null>(null)
  const [devices, setDevices] = useState<DeviceDocument[]>([])
  const [selected, setSelected] = useState('')
  const [diff, setDiff] = useState<DiffLine[]>([])
  const [fromVersion, setFromVersion] = useState(1)
  const [toVersion, setToVersion] = useState(2)
  const [error, setError] = useState('')
  const [name, setName] = useState('')
  const [comment, setComment] = useState('')
  const [content, setContent] = useState('O0001\nG90 G54\nM30\n')
  const [deviceId, setDeviceId] = useState('')
  const [channel, setChannel] = useState('dnc-folder')
  const [folder, setFolder] = useState('')

  async function load(prefer?: string) {
    const [body, list] = await Promise.all([
      studioApi<ProgramsView>('/api/v1/programs'),
      studioApi<DeviceDocument[]>('/api/v1/config/devices'),
    ])
    setView(body)
    setDevices(list)
    const id = prefer || selected || body.programs?.[0]?.id || ''
    if (id) setSelected(id)
    if (!deviceId && list[0]) setDeviceId(list[0].metadata.id)
    const program = body.programs?.find((item) => item.id === id) ?? body.programs?.[0]
    if (program && program.versions.length >= 2) {
      setFromVersion(program.versions[0].version)
      setToVersion(program.versions[program.versions.length - 1].version)
    }
    return { body, id: program?.id ?? '' }
  }

  async function loadDiff(id: string, from: number, to: number) {
    if (!id) return
    const body = await studioApi<{ lines: DiffLine[] }>(`/api/v1/programs/${encodeURIComponent(id)}/diff?from=${from}&to=${to}`)
    setDiff(body.lines ?? [])
  }

  useEffect(() => {
    void load()
      .then((result) => {
        if (result.id) return loadDiff(result.id, 1, 2)
        return undefined
      })
      .catch((err: unknown) => setError(describeError(err)))
  }, [])

  const program = view?.programs?.find((item) => item.id === selected)

  async function upload() {
    const created = await studioApi<{ programId: string }>('/api/v1/programs', {
      method: 'POST',
      body: JSON.stringify({ name, comment, content, deviceIds: deviceId ? [deviceId] : [] }),
    })
    setName('')
    const result = await load(created.programId)
    if (result.id) await loadDiff(result.id, fromVersion, toVersion)
  }

  async function addVersion() {
    if (!program) return
    await studioApi(`/api/v1/programs/${encodeURIComponent(program.id)}/versions`, {
      method: 'POST',
      body: JSON.stringify({ content, comment }),
    })
    await load(program.id)
  }

  async function approve(versionId: string) {
    if (!program) return
    await studioApi(`/api/v1/programs/${encodeURIComponent(program.id)}/approve`, {
      method: 'POST',
      body: JSON.stringify({ versionId }),
    })
    await load(program.id)
  }

  async function send() {
    if (!program) return
    const version = program.versions.find((item) => item.status === 'approved') ?? program.versions[program.versions.length - 1]
    await studioApi(`/api/v1/programs/${encodeURIComponent(program.id)}/send`, {
      method: 'POST',
      body: JSON.stringify({ versionId: version?.id, deviceId, channel, folder: folder || null }),
    })
    await load(program.id)
  }

  return (
    <PageShell
      title='NC 程序'
      description='程序库按版本保存，校验和是换行归一后的 SHA-256。未批准的版本不能下发。只有现有驱动真正支持的通道，或通用 DNC / FTP / 已挂载共享目录，才会发送。'
    >
      {error ? <p className='mb-3 text-sm text-destructive'>{error}</p> : null}
      {view && !view.licensed ? <p className='text-sm text-muted-foreground'>{view.message || '当前许可证不包含 NC 程序。'}</p> : null}
      {view?.licensed ? (
        <div className='grid gap-4'>
          <div className='grid gap-4 lg:grid-cols-2'>
            <Card>
              <CardHeader><CardTitle className='text-base'>程序库</CardTitle></CardHeader>
              <CardContent className='grid gap-3'>
                {(view.programs ?? []).map((item) => (
                  <button
                    key={item.id}
                    type='button'
                    className={`rounded-md border px-3 py-2 text-left text-sm ${item.id === selected ? 'border-primary' : ''}`}
                    onClick={() => {
                      setSelected(item.id)
                      const from = item.versions[0]?.version ?? 1
                      const to = item.versions[item.versions.length - 1]?.version ?? from
                      setFromVersion(from)
                      setToVersion(to)
                      void loadDiff(item.id, from, to).catch((err: unknown) => setError(describeError(err)))
                    }}
                  >
                    <span className='font-medium'>{item.name}</span>
                    <span className='ms-2 text-muted-foreground'>{statusText[item.status] ?? item.status}</span>
                    <span className='ms-2 text-muted-foreground'>{item.versions.length} 个版本</span>
                  </button>
                ))}
                <div className='grid gap-2'>
                  <Label>新程序名</Label>
                  <Input value={name} onChange={(event) => setName(event.target.value)} />
                  <Label>注释</Label>
                  <Input value={comment} onChange={(event) => setComment(event.target.value)} />
                  <Label>内容</Label>
                  <Textarea value={content} onChange={(event) => setContent(event.target.value)} rows={6} />
                  <div className='flex gap-2'>
                    <Button disabled={!writable} onClick={() => void upload().catch((err: unknown) => setError(describeError(err)))}>上传草稿</Button>
                    <Button variant='outline' disabled={!writable || !program} onClick={() => void addVersion().catch((err: unknown) => setError(describeError(err)))}>另存为新版本</Button>
                  </div>
                </div>
              </CardContent>
            </Card>

            <Card>
              <CardHeader><CardTitle className='text-base'>版本与批准</CardTitle></CardHeader>
              <CardContent data-testid='program-approval'>
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>版本</TableHead>
                      <TableHead>状态</TableHead>
                      <TableHead>校验和</TableHead>
                      <TableHead />
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {(program?.versions ?? []).map((version) => (
                      <TableRow key={version.id}>
                        <TableCell>v{version.version}</TableCell>
                        <TableCell>
                          <Badge variant={version.status === 'approved' ? 'default' : 'outline'}>{statusText[version.status] ?? version.status}</Badge>
                          <div className='text-xs text-muted-foreground'>{version.comment}</div>
                        </TableCell>
                        <TableCell className='max-w-40 truncate font-mono text-xs' title={version.checksum}>{version.checksum.slice(0, 12)}</TableCell>
                        <TableCell>
                          <Button size='sm' variant='outline' disabled={!admin || version.status === 'approved'} onClick={() => void approve(version.id).catch((err: unknown) => setError(describeError(err)))}>
                            批准
                          </Button>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
                <p className='mt-2 text-xs text-muted-foreground'>工程师上传。管理员批准。批准人 {program?.versions.find((item) => item.approvedBy)?.approvedBy || '—'}。</p>
              </CardContent>
            </Card>
          </div>

          <Card>
            <CardHeader className='flex flex-row items-center justify-between'>
              <CardTitle className='text-base'>版本差异</CardTitle>
              <Button
                variant='outline'
                size='sm'
                disabled={!program}
                onClick={() => program && void loadDiff(program.id, fromVersion, toVersion).catch((err: unknown) => setError(describeError(err)))}
              >
                对比 v{fromVersion} / v{toVersion}
              </Button>
            </CardHeader>
            <CardContent data-testid='program-diff' className='font-mono text-xs'>
              {diff.map((line, index) => (
                <div
                  key={`${index}-${line.kind}`}
                  className={line.kind === 'add' ? 'bg-emerald-500/15' : line.kind === 'remove' ? 'bg-red-500/15' : ''}
                >
                  {line.kind === 'add' ? '+' : line.kind === 'remove' ? '-' : ' '}
                  {line.text}
                </div>
              ))}
            </CardContent>
          </Card>

          <Card>
            <CardHeader><CardTitle className='text-base'>下发到设备</CardTitle></CardHeader>
            <CardContent className='grid gap-3 sm:grid-cols-4'>
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
              <Select value={channel} onValueChange={setChannel}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value='dnc-folder'>DNC 目录</SelectItem>
                  <SelectItem value='smb-folder'>已挂载共享目录</SelectItem>
                  <SelectItem value='ftp'>FTP</SelectItem>
                </SelectContent>
              </Select>
              <Input placeholder='目录，可空' value={folder} onChange={(event) => setFolder(event.target.value)} />
              <Button disabled={!writable} onClick={() => void send().catch((err: unknown) => setError(describeError(err)))}>下发已批准版本</Button>
            </CardContent>
          </Card>

          <Card>
            <CardHeader><CardTitle className='text-base'>各品牌传输能力</CardTitle></CardHeader>
            <CardContent>
              <Table data-testid='transfer-capability'>
                <TableHeader>
                  <TableRow>
                    <TableHead>品牌</TableHead>
                    <TableHead>通道</TableHead>
                    <TableHead>能否下发</TableHead>
                    <TableHead>说明</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {(view.brands ?? []).flatMap((brand) =>
                    brand.channels.map((item) => (
                      <TableRow key={`${brand.brandId}-${item.channel}-${item.adapterId}`}>
                        <TableCell>{brand.nameZh}</TableCell>
                        <TableCell>{item.channel}</TableCell>
                        <TableCell>{item.canTransfer ? '可以' : '不可以'}</TableCell>
                        <TableCell className='max-w-xl text-sm'>{item.note}</TableCell>
                      </TableRow>
                    ))
                  )}
                </TableBody>
              </Table>
            </CardContent>
          </Card>

          <Card>
            <CardHeader><CardTitle className='text-base'>传输记录</CardTitle></CardHeader>
            <CardContent>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>时间</TableHead>
                    <TableHead>方向</TableHead>
                    <TableHead>通道</TableHead>
                    <TableHead>结果</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {(view.transfers ?? []).map((item, index) => (
                    <TableRow key={`${item.unixMs}-${index}`}>
                      <TableCell>{formatTime(item.unixMs)}</TableCell>
                      <TableCell>{item.direction}</TableCell>
                      <TableCell>{item.channel}</TableCell>
                      <TableCell>{item.result} {item.message}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        </div>
      ) : null}
    </PageShell>
  )
}
