import { useEffect, useState } from 'react'
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
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { PageShell } from '@/features/daq/page-shell'
import { formatTime } from '@/features/viz/format'
import { canWrite, describeError, studioApi, type DeviceDocument } from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'

type Shift = { name: string; start: string; end: string; plannedMinutes: number }
type BreakRow = { name: string; start: string; end: string }
type Calendar = { timeZone: string; shifts: Shift[]; breaks: BreakRow[]; holidays: string[] }
type Planned = { id: string; scope: string; ownerId: string; name: string; startUnixMs: number; endUnixMs: number }
type Cycle = { id: string; ownerId: string; program: string; idealSeconds: number }
type StateMap = { id: string; scope: string; ownerId: string; rawValue: string; state: string }

const states = [
  ['running', '运行'],
  ['idle', '待机'],
  ['alarm', '报警/故障'],
  ['setup', '换型'],
  ['waiting', '待料'],
  ['planned', '计划停机'],
  ['stopped', '停机'],
  ['offline', '离线'],
] as const

function localInput(ms = Date.now()) {
  const date = new Date(ms)
  const pad = (value: number) => String(value).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
}

export function CalendarPage() {
  const writable = canWrite(useAuthStore((state) => state.auth.user?.role[0]))
  const [calendar, setCalendar] = useState<Calendar>({ timeZone: 'Asia/Shanghai', shifts: [], breaks: [], holidays: [] })
  const [holiday, setHoliday] = useState('')
  const [planned, setPlanned] = useState<Planned[]>([])
  const [cycles, setCycles] = useState<Cycle[]>([])
  const [maps, setMaps] = useState<StateMap[]>([])
  const [devices, setDevices] = useState<DeviceDocument[]>([])
  const [stop, setStop] = useState({ scope: 'device', ownerId: '', name: '计划保养', start: '', end: '' })
  const [cycle, setCycle] = useState({ ownerId: '', program: '', idealSeconds: '48' })
  const [stateMap, setStateMap] = useState({ scope: 'brand', ownerId: '', rawValue: '', state: 'setup' })
  const [error, setError] = useState('')

  async function load() {
    const [body, deviceList] = await Promise.all([
      studioApi<{ calendar: Calendar; planned: Planned[]; cycles: Cycle[]; maps: StateMap[] }>('/api/v1/oee/calendar'),
      studioApi<DeviceDocument[]>('/api/v1/config/devices'),
    ])
    setCalendar({
      timeZone: body.calendar.timeZone || 'Asia/Shanghai',
      shifts: body.calendar.shifts ?? [],
      breaks: body.calendar.breaks ?? [],
      holidays: body.calendar.holidays ?? [],
    })
    setPlanned(body.planned)
    setCycles(body.cycles)
    setMaps(body.maps)
    setDevices(deviceList)
    const first = deviceList[0]?.metadata.id ?? ''
    setStop((current) => ({
      ...current,
      ownerId: current.ownerId || first,
      start: current.start || localInput(),
      end: current.end || localInput(Date.now() + 3600000),
    }))
    setCycle((current) => ({ ...current, ownerId: current.ownerId || first }))
  }

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load().catch((err: unknown) => setError(describeError(err)))
  }, [])

  async function saveCalendar() {
    try {
      await studioApi('/api/v1/oee/calendar', { method: 'PUT', body: JSON.stringify({ calendar }) })
      setError('')
      await load()
    } catch (err) {
      setError(describeError(err))
    }
  }

  async function addStop() {
    try {
      await studioApi(`/api/v1/oee/planned/${crypto.randomUUID().replace(/-/g, '')}`, {
        method: 'PUT',
        body: JSON.stringify({
          scope: stop.scope,
          ownerId: stop.ownerId,
          name: stop.name,
          startUnixMs: new Date(stop.start).getTime(),
          endUnixMs: new Date(stop.end).getTime(),
        }),
      })
      setError('')
      await load()
    } catch (err) {
      setError(describeError(err))
    }
  }

  async function addCycle() {
    try {
      await studioApi(`/api/v1/oee/cycles/${crypto.randomUUID().replace(/-/g, '')}`, {
        method: 'PUT',
        body: JSON.stringify({ ownerId: cycle.ownerId, program: cycle.program, idealSeconds: Number(cycle.idealSeconds) }),
      })
      setError('')
      await load()
    } catch (err) {
      setError(describeError(err))
    }
  }

  async function addMap() {
    try {
      await studioApi(`/api/v1/oee/states/${crypto.randomUUID().replace(/-/g, '')}`, {
        method: 'PUT',
        body: JSON.stringify(stateMap),
      })
      setError('')
      await load()
    } catch (err) {
      setError(describeError(err))
    }
  }

  async function remove(path: string) {
    try {
      await studioApi(path, { method: 'DELETE' })
      await load()
    } catch (err) {
      setError(describeError(err))
    }
  }

  const lines = [...new Set(devices.map((device) => device.spec.line).filter((line): line is string => Boolean(line)))]

  return (
    <PageShell
      title='班次日历'
      description='班次、休息、节假日和计划停机决定计划生产时间。配置了休息时段后，计划时间按班次长度减去休息，不再叠加计划分钟。理想节拍按设备，程序名可空表示默认节拍。'
    >
      {error ? <p className='mb-3 text-sm text-destructive'>{error}</p> : null}
      <div className='grid gap-4 lg:grid-cols-2' data-testid='shift-calendar'>
        <Card>
          <CardHeader><CardTitle>班次</CardTitle></CardHeader>
          <CardContent className='grid gap-3'>
            {calendar.shifts.map((shift, index) => (
              <div key={index} className='grid grid-cols-4 gap-2'>
                <Input value={shift.name} disabled={!writable} onChange={(event) => setCalendar({ ...calendar, shifts: calendar.shifts.map((item, i) => i === index ? { ...item, name: event.target.value } : item) })} />
                <Input value={shift.start} disabled={!writable} onChange={(event) => setCalendar({ ...calendar, shifts: calendar.shifts.map((item, i) => i === index ? { ...item, start: event.target.value } : item) })} />
                <Input value={shift.end} disabled={!writable} onChange={(event) => setCalendar({ ...calendar, shifts: calendar.shifts.map((item, i) => i === index ? { ...item, end: event.target.value } : item) })} />
                <Input type='number' value={shift.plannedMinutes} disabled={!writable} onChange={(event) => setCalendar({ ...calendar, shifts: calendar.shifts.map((item, i) => i === index ? { ...item, plannedMinutes: Number(event.target.value) } : item) })} />
              </div>
            ))}
            <Button variant='outline' size='sm' className='w-fit' disabled={!writable} onClick={() => setCalendar({ ...calendar, shifts: [...calendar.shifts, { name: '新班次', start: '08:00', end: '16:00', plannedMinutes: 480 }] })}>添加班次</Button>
          </CardContent>
        </Card>
        <Card>
          <CardHeader><CardTitle>休息与节假日</CardTitle></CardHeader>
          <CardContent className='grid gap-3'>
            {calendar.breaks.map((item, index) => (
              <div key={index} className='grid grid-cols-3 gap-2'>
                <Input value={item.name} disabled={!writable} onChange={(event) => setCalendar({ ...calendar, breaks: calendar.breaks.map((row, i) => i === index ? { ...row, name: event.target.value } : row) })} />
                <Input value={item.start} disabled={!writable} onChange={(event) => setCalendar({ ...calendar, breaks: calendar.breaks.map((row, i) => i === index ? { ...row, start: event.target.value } : row) })} />
                <Input value={item.end} disabled={!writable} onChange={(event) => setCalendar({ ...calendar, breaks: calendar.breaks.map((row, i) => i === index ? { ...row, end: event.target.value } : row) })} />
              </div>
            ))}
            <Button variant='outline' size='sm' className='w-fit' disabled={!writable} onClick={() => setCalendar({ ...calendar, breaks: [...calendar.breaks, { name: '休息', start: '12:00', end: '13:00' }] })}>添加休息</Button>
            <div className='flex flex-wrap gap-2'>
              {calendar.holidays.map((day) => (
                <Button key={day} variant='secondary' size='sm' disabled={!writable} onClick={() => setCalendar({ ...calendar, holidays: calendar.holidays.filter((item) => item !== day) })}>{day}</Button>
              ))}
            </div>
            <div className='flex gap-2'>
              <Input type='date' value={holiday} disabled={!writable} onChange={(event) => setHoliday(event.target.value)} />
              <Button variant='outline' disabled={!writable || !holiday} onClick={() => { setCalendar({ ...calendar, holidays: [...calendar.holidays, holiday] }); setHoliday('') }}>添加节假日</Button>
            </div>
            <Button disabled={!writable} onClick={() => void saveCalendar()}>保存日历</Button>
          </CardContent>
        </Card>
      </div>
      <div className='mt-4 grid gap-4 lg:grid-cols-2'>
        <Card>
          <CardHeader><CardTitle>计划停机</CardTitle></CardHeader>
          <CardContent className='grid gap-3'>
            <div className='grid gap-2 sm:grid-cols-2'>
              <Input value={stop.name} disabled={!writable} onChange={(event) => setStop({ ...stop, name: event.target.value })} />
              <Select value={stop.scope} disabled={!writable} onValueChange={(scope) => setStop({ ...stop, scope, ownerId: '' })}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value='device'>设备</SelectItem>
                  <SelectItem value='line'>产线</SelectItem>
                </SelectContent>
              </Select>
              <Select value={stop.ownerId || 'none'} disabled={!writable} onValueChange={(ownerId) => setStop({ ...stop, ownerId: ownerId === 'none' ? '' : ownerId })}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value='none'>选择</SelectItem>
                  {(stop.scope === 'line' ? lines : devices.map((device) => device.metadata.id)).map((id) => <SelectItem key={id} value={id}>{id}</SelectItem>)}
                </SelectContent>
              </Select>
              <Input type='datetime-local' value={stop.start} disabled={!writable} onChange={(event) => setStop({ ...stop, start: event.target.value })} />
              <Input type='datetime-local' value={stop.end} disabled={!writable} onChange={(event) => setStop({ ...stop, end: event.target.value })} />
            </div>
            <Button variant='outline' className='w-fit' disabled={!writable} onClick={() => void addStop()}>添加计划停机</Button>
            <Table>
              <TableBody>
                {planned.map((item) => (
                  <TableRow key={item.id}>
                    <TableCell>{item.name}</TableCell>
                    <TableCell>{item.ownerId}</TableCell>
                    <TableCell>{formatTime(item.startUnixMs)} – {formatTime(item.endUnixMs)}</TableCell>
                    <TableCell><Button variant='ghost' size='sm' disabled={!writable} onClick={() => void remove(`/api/v1/oee/planned/${item.id}`)}>删除</Button></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
        <Card>
          <CardHeader><CardTitle>理想节拍与状态映射</CardTitle></CardHeader>
          <CardContent className='grid gap-3'>
            <div className='grid gap-2 sm:grid-cols-3'>
              <Select value={cycle.ownerId || 'none'} disabled={!writable} onValueChange={(ownerId) => setCycle({ ...cycle, ownerId: ownerId === 'none' ? '' : ownerId })}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value='none'>设备</SelectItem>
                  {devices.map((device) => <SelectItem key={device.metadata.id} value={device.metadata.id}>{device.metadata.displayName || device.metadata.id}</SelectItem>)}
                </SelectContent>
              </Select>
              <Input placeholder='程序，空为默认' disabled={!writable} value={cycle.program} onChange={(event) => setCycle({ ...cycle, program: event.target.value })} />
              <Input type='number' disabled={!writable} value={cycle.idealSeconds} onChange={(event) => setCycle({ ...cycle, idealSeconds: event.target.value })} />
            </div>
            <Button variant='outline' className='w-fit' disabled={!writable} onClick={() => void addCycle()}>保存节拍（秒）</Button>
            <Table>
              <TableHeader><TableRow><TableHead>设备</TableHead><TableHead>程序</TableHead><TableHead>秒</TableHead><TableHead /></TableRow></TableHeader>
              <TableBody>
                {cycles.map((item) => (
                  <TableRow key={item.id}>
                    <TableCell>{item.ownerId}</TableCell>
                    <TableCell>{item.program || '默认'}</TableCell>
                    <TableCell>{item.idealSeconds}</TableCell>
                    <TableCell><Button variant='ghost' size='sm' disabled={!writable} onClick={() => void remove(`/api/v1/oee/cycles/${item.id}`)}>删除</Button></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
            <div className='grid gap-2 sm:grid-cols-4'>
              <Select value={stateMap.scope} disabled={!writable} onValueChange={(scope) => setStateMap({ ...stateMap, scope })}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value='brand'>品牌</SelectItem>
                  <SelectItem value='template'>模板</SelectItem>
                  <SelectItem value='device'>设备</SelectItem>
                </SelectContent>
              </Select>
              <Input placeholder='品牌 / 模板 / 设备' disabled={!writable} value={stateMap.ownerId} onChange={(event) => setStateMap({ ...stateMap, ownerId: event.target.value })} />
              <Input placeholder='原始状态字' disabled={!writable} value={stateMap.rawValue} onChange={(event) => setStateMap({ ...stateMap, rawValue: event.target.value })} />
              <Select value={stateMap.state} disabled={!writable} onValueChange={(state) => setStateMap({ ...stateMap, state })}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  {states.map(([value, label]) => <SelectItem key={value} value={value}>{label}</SelectItem>)}
                </SelectContent>
              </Select>
            </div>
            <Button variant='outline' className='w-fit' disabled={!writable} onClick={() => void addMap()}>保存状态映射</Button>
            <Table>
              <TableBody>
                {maps.map((item) => (
                  <TableRow key={item.id}>
                    <TableCell>{item.scope} {item.ownerId}</TableCell>
                    <TableCell>{item.rawValue} → {states.find((pair) => pair[0] === item.state)?.[1] || item.state}</TableCell>
                    <TableCell><Button variant='ghost' size='sm' disabled={!writable} onClick={() => void remove(`/api/v1/oee/states/${item.id}`)}>删除</Button></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      </div>
    </PageShell>
  )
}
