import { useEffect, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
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
import { formatMinutes, formatTime } from '@/features/viz/format'
import { canOperate, canWrite, describeError, studioApi } from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'

type Reason = { id: string; parentId: string | null; code: string; name: string; sort: number; enabled: boolean }
type EventRow = {
  id: string
  deviceId: string
  state: string
  startedUnixMs: number
  endedUnixMs: number | null
  reasonId: string | null
  note: string
  source: string
}

const stateLabel: Record<string, string> = {
  idle: '待机',
  alarm: '报警',
  offline: '离线',
  stopped: '停机',
  setup: '换型',
  waiting: '待料',
  planned: '计划停机',
}

function isoDate(date = new Date()) {
  const pad = (value: number) => String(value).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

export function DowntimePage() {
  const role = useAuthStore((state) => state.auth.user?.role[0])
  const operable = canOperate(role)
  const writable = canWrite(role)
  const [reasons, setReasons] = useState<Reason[]>([])
  const [events, setEvents] = useState<EventRow[]>([])
  const [from, setFrom] = useState(() => isoDate(new Date(Date.now() - 86400000)))
  const [to, setTo] = useState(() => isoDate())
  const [picked, setPicked] = useState<string[]>([])
  const [reasonId, setReasonId] = useState('')
  const [note, setNote] = useState('')
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [parentId, setParentId] = useState('none')
  const [error, setError] = useState('')
  const [saved, setSaved] = useState('')

  async function load() {
    const start = new Date(`${from}T00:00:00`).getTime()
    const end = new Date(`${to}T23:59:59`).getTime()
    const body = await studioApi<{ reasons: Reason[]; events: EventRow[] }>(`/api/v1/downtime?from=${start}&to=${end}`)
    setReasons(body.reasons)
    setEvents(body.events)
    setReasonId((current) => current || body.reasons.find((item) => item.parentId)?.id || body.reasons[0]?.id || '')
  }

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load().catch((err: unknown) => setError(describeError(err)))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  function toggle(id: string, checked: boolean) {
    setPicked((current) => checked ? [...current, id] : current.filter((item) => item !== id))
  }

  async function assign(bulk: boolean) {
    const ids = bulk ? picked : picked.slice(0, 1)
    try {
      const body = await studioApi<{ updated: number }>(bulk ? '/api/v1/downtime/bulk' : '/api/v1/downtime/assign', {
        method: 'POST',
        body: JSON.stringify({ eventIds: ids.length ? ids : picked, reasonId, note }),
      })
      setSaved(`已填写 ${body.updated} 条`)
      setError('')
      setPicked([])
      await load()
    } catch (err) {
      setError(describeError(err))
    }
  }

  async function addReason() {
    try {
      await studioApi(`/api/v1/oee/reasons/${crypto.randomUUID().replace(/-/g, '')}`, {
        method: 'PUT',
        body: JSON.stringify({ code, name, parentId: parentId === 'none' ? null : parentId, sort: reasons.length, enabled: true }),
      })
      setCode('')
      setName('')
      setError('')
      await load()
    } catch (err) {
      setError(describeError(err))
    }
  }

  const reasonName = (id: string | null) => reasons.find((item) => item.id === id)?.name || '未说明'

  return (
    <PageShell
      title='停机原因'
      description='操作员可以为停机记录填写原因，不能修改原因树、班次或规则。工程师可以维护原因树，也可以批量指定。'
    >
      {error ? <p className='mb-3 text-sm text-destructive'>{error}</p> : null}
      {saved ? <p className='mb-3 text-sm text-muted-foreground'>{saved}</p> : null}
      <Card className='mb-4' data-testid='reason-entry'>
        <CardHeader><CardTitle>填写原因</CardTitle></CardHeader>
        <CardContent className='flex flex-wrap items-end gap-3'>
          <label className='grid gap-1 text-xs text-muted-foreground'>
            从
            <Input type='date' value={from} onChange={(event) => setFrom(event.target.value)} />
          </label>
          <label className='grid gap-1 text-xs text-muted-foreground'>
            到
            <Input type='date' value={to} onChange={(event) => setTo(event.target.value)} />
          </label>
          <Button variant='outline' onClick={() => void load().catch((err: unknown) => setError(describeError(err)))}>查询</Button>
          <label className='grid gap-1 text-xs text-muted-foreground'>
            原因
            <Select value={reasonId || 'none'} onValueChange={(value) => setReasonId(value === 'none' ? '' : value)} disabled={!operable}>
              <SelectTrigger className='w-56'><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value='none'>选择原因</SelectItem>
                {reasons.filter((item) => item.enabled).map((item) => (
                  <SelectItem key={item.id} value={item.id}>{item.code} {item.name}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </label>
          <label className='grid gap-1 text-xs text-muted-foreground'>
            备注
            <Input value={note} disabled={!operable} onChange={(event) => setNote(event.target.value)} />
          </label>
          <Button disabled={!operable || picked.length === 0} onClick={() => void assign(false)}>填写选中</Button>
          <Button variant='outline' disabled={!operable || picked.length < 2} onClick={() => void assign(true)}>批量指定</Button>
        </CardContent>
      </Card>
      <Card>
        <CardContent className='pt-4'>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead />
                <TableHead>设备</TableHead>
                <TableHead>状态</TableHead>
                <TableHead>开始</TableHead>
                <TableHead>时长（分）</TableHead>
                <TableHead>原因</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {events.length === 0 ? <TableRow><TableCell colSpan={6} className='text-muted-foreground'>这个时间段没有停机记录</TableCell></TableRow> : events.map((item) => (
                <TableRow key={item.id}>
                  <TableCell>
                    <Checkbox checked={picked.includes(item.id)} onCheckedChange={(checked) => toggle(item.id, checked === true)} />
                  </TableCell>
                  <TableCell>{item.deviceId}</TableCell>
                  <TableCell>{stateLabel[item.state] || item.state}</TableCell>
                  <TableCell>{formatTime(item.startedUnixMs)}</TableCell>
                  <TableCell>{item.endedUnixMs ? formatMinutes(item.endedUnixMs - item.startedUnixMs) : '进行中'}</TableCell>
                  <TableCell>{reasonName(item.reasonId)}{item.note ? `（${item.note}）` : ''}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
      <Card className='mt-4'>
        <CardHeader><CardTitle>原因树</CardTitle></CardHeader>
        <CardContent className='grid gap-3'>
          <ul className='grid gap-1 text-sm'>
            {reasons.filter((item) => !item.parentId).map((parent) => (
              <li key={parent.id}>
                <span className='font-medium'>{parent.name}</span>
                <span className='text-muted-foreground'> {reasons.filter((item) => item.parentId === parent.id).map((item) => `${item.code} ${item.name}`).join('、')}</span>
              </li>
            ))}
          </ul>
          {writable ? (
            <div className='flex flex-wrap items-end gap-2'>
              <Input className='w-28' placeholder='编码' value={code} onChange={(event) => setCode(event.target.value)} />
              <Input className='w-40' placeholder='名称' value={name} onChange={(event) => setName(event.target.value)} />
              <Select value={parentId} onValueChange={setParentId}>
                <SelectTrigger className='w-40'><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value='none'>作为一级</SelectItem>
                  {reasons.filter((item) => !item.parentId).map((item) => <SelectItem key={item.id} value={item.id}>{item.name}</SelectItem>)}
                </SelectContent>
              </Select>
              <Button variant='outline' onClick={() => void addReason()}>添加原因</Button>
            </div>
          ) : <p className='text-xs text-muted-foreground'>当前角色只能填写原因，不能改原因树。</p>}
        </CardContent>
      </Card>
    </PageShell>
  )
}
