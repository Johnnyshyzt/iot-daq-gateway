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
import { Textarea } from '@/components/ui/textarea'
import { PageShell } from '@/features/daq/page-shell'
import { formatTime } from '@/features/viz/format'
import {
  canWrite,
  describeError,
  studioApi,
  type DeviceDocument,
  type PointTemplateDocument,
} from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'

type RuleAction = {
  type: string
  severity: string
  message: string
  code: string
  channelId: string
  pointId: string
  value?: number | null
  name: string
  reasonCode: string
}

type EdgeRule = {
  id: string
  name: string
  enabled: boolean
  scope: string
  ownerId: string
  expression: string
  durationMs: number
  debounceMs: number
  actionsJson: string
  templateKey: string
}

type Template = {
  key: string
  name: string
  expression: string
  durationMs: number
  debounceMs: number
  description: string
  actionsJson: string
}

type LogRow = {
  id: number
  ruleId: string
  deviceId: string
  unixMs: number
  fired: boolean
  message: string
}

type BacktestHit = { unixMs: number; fired: boolean; message?: string }

const emptyAction = (): RuleAction => ({
  type: 'alarm',
  severity: 'warning',
  message: '',
  code: '',
  channelId: '',
  pointId: '',
  value: 1,
  name: '',
  reasonCode: '',
})

function parseActions(json: string): RuleAction[] {
  try {
    const parsed = JSON.parse(json) as RuleAction[]
    return Array.isArray(parsed) && parsed.length > 0 ? parsed.map((item) => ({ ...emptyAction(), ...item })) : [emptyAction()]
  } catch {
    return [emptyAction()]
  }
}

function blank(): EdgeRule {
  return {
    id: crypto.randomUUID().replace(/-/g, ''),
    name: '',
    enabled: true,
    scope: 'all',
    ownerId: '',
    expression: '',
    durationMs: 0,
    debounceMs: 0,
    actionsJson: '[]',
    templateKey: '',
  }
}

export function RulesPage() {
  const writable = canWrite(useAuthStore((state) => state.auth.user?.role[0]))
  const [licensed, setLicensed] = useState(true)
  const [rules, setRules] = useState<EdgeRule[]>([])
  const [templates, setTemplates] = useState<Template[]>([])
  const [devices, setDevices] = useState<DeviceDocument[]>([])
  const [pointTemplates, setPointTemplates] = useState<PointTemplateDocument[]>([])
  const [draft, setDraft] = useState<EdgeRule>(blank())
  const [actions, setActions] = useState<RuleAction[]>([emptyAction()])
  const [logs, setLogs] = useState<LogRow[]>([])
  const [backtestDevice, setBacktestDevice] = useState('')
  const [hits, setHits] = useState<BacktestHit[]>([])
  const [backtestSummary, setBacktestSummary] = useState('')
  const [error, setError] = useState('')

  async function load() {
    const [ruleBody, deviceList, templateList, logBody] = await Promise.all([
      studioApi<{ licensed: boolean; rules: EdgeRule[]; templates: Template[] }>('/api/v1/rules'),
      studioApi<DeviceDocument[]>('/api/v1/config/devices'),
      studioApi<PointTemplateDocument[]>('/api/v1/config/point-templates'),
      studioApi<{ events: LogRow[] }>('/api/v1/rules/log?limit=30'),
    ])
    setLicensed(ruleBody.licensed)
    setRules(ruleBody.rules)
    setTemplates(ruleBody.templates)
    setDevices(deviceList)
    setPointTemplates(templateList)
    setLogs(logBody.events)
    setBacktestDevice((current) => current || deviceList[0]?.metadata.id || '')
  }

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load().catch((err: unknown) => setError(describeError(err)))
  }, [])

  function edit(rule: EdgeRule) {
    setDraft(rule)
    setActions(parseActions(rule.actionsJson))
    setHits([])
    setBacktestSummary('')
  }

  function applyTemplate(template: Template) {
    setDraft({
      ...blank(),
      name: template.name,
      expression: template.expression,
      durationMs: template.durationMs,
      debounceMs: template.debounceMs,
      actionsJson: template.actionsJson,
      templateKey: template.key,
    })
    setActions(parseActions(template.actionsJson))
  }

  async function save() {
    const body = { ...draft, actionsJson: JSON.stringify(actions) }
    try {
      await studioApi(`/api/v1/rules/${draft.id}`, { method: 'PUT', body: JSON.stringify(body) })
      setError('')
      await load()
    } catch (err) {
      setError(describeError(err))
    }
  }

  async function remove(id: string) {
    try {
      await studioApi(`/api/v1/rules/${id}`, { method: 'DELETE' })
      if (draft.id === id) {
        setDraft(blank())
        setActions([emptyAction()])
      }
      await load()
    } catch (err) {
      setError(describeError(err))
    }
  }

  async function backtest() {
    try {
      const result = await studioApi<{ frames: number; fires: number; hits: BacktestHit[] }>('/api/v1/rules/backtest', {
        method: 'POST',
        body: JSON.stringify({
          expression: draft.expression,
          deviceId: backtestDevice,
          durationMs: Number(draft.durationMs) || 0,
          debounceMs: Number(draft.debounceMs) || 0,
        }),
      })
      setHits(result.hits.filter((item) => item.fired).slice(0, 12))
      setBacktestSummary(`最近历史 ${result.frames} 帧，触发 ${result.fires} 次`)
      setError('')
    } catch (err) {
      setError(describeError(err))
    }
  }

  const owners = draft.scope === 'template'
    ? pointTemplates.map((item) => ({ id: item.metadata.id, label: item.metadata.displayName || item.metadata.id }))
    : devices.map((item) => ({ id: item.metadata.id, label: item.metadata.displayName || item.metadata.id }))

  return (
    <PageShell
      title='边缘规则'
      description='条件表达式持续满足一段时间后执行动作：报警、通知、写入、北向事件或自动填写停机原因。规则引擎是商业功能。'
    >
      {!licensed ? <p className='mb-3 text-sm text-destructive'>当前授权未包含边缘规则引擎。可以查看模板，保存和回测需要许可。</p> : null}
      {error ? <p className='mb-3 text-sm text-destructive'>{error}</p> : null}
      <div className='mb-4 flex flex-wrap gap-2'>
        {templates.map((template) => (
          <Button key={template.key} variant='outline' size='sm' disabled={!writable} onClick={() => applyTemplate(template)}>
            {template.name}
          </Button>
        ))}
      </div>
      <div className='grid gap-4 lg:grid-cols-[1.15fr_0.85fr]'>
        <Card>
          <CardHeader><CardTitle>规则</CardTitle></CardHeader>
          <CardContent className='grid gap-3'>
            <div className='grid gap-3 sm:grid-cols-2'>
              <label className='grid gap-1 text-xs text-muted-foreground'>
                名称
                <Input value={draft.name} disabled={!writable} onChange={(event) => setDraft({ ...draft, name: event.target.value })} />
              </label>
              <label className='grid gap-1 text-xs text-muted-foreground'>
                启用
                <Select value={draft.enabled ? 'yes' : 'no'} disabled={!writable} onValueChange={(value) => setDraft({ ...draft, enabled: value === 'yes' })}>
                  <SelectTrigger><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value='yes'>启用</SelectItem>
                    <SelectItem value='no'>停用</SelectItem>
                  </SelectContent>
                </Select>
              </label>
              <label className='grid gap-1 text-xs text-muted-foreground'>
                范围
                <Select value={draft.scope} disabled={!writable} onValueChange={(scope) => setDraft({ ...draft, scope, ownerId: '' })}>
                  <SelectTrigger><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value='all'>全部设备</SelectItem>
                    <SelectItem value='device'>单台设备</SelectItem>
                    <SelectItem value='template'>点位模板</SelectItem>
                  </SelectContent>
                </Select>
              </label>
              {draft.scope === 'all' ? null : (
                <label className='grid gap-1 text-xs text-muted-foreground'>
                  对象
                  <Select value={draft.ownerId || 'none'} disabled={!writable} onValueChange={(ownerId) => setDraft({ ...draft, ownerId: ownerId === 'none' ? '' : ownerId })}>
                    <SelectTrigger><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem value='none'>请选择</SelectItem>
                      {owners.map((item) => <SelectItem key={item.id} value={item.id}>{item.label}</SelectItem>)}
                    </SelectContent>
                  </Select>
                </label>
              )}
              <label className='grid gap-1 text-xs text-muted-foreground'>
                持续（分钟）
                <Input type='number' min={0} disabled={!writable} value={Math.round(draft.durationMs / 60000)} onChange={(event) => setDraft({ ...draft, durationMs: Number(event.target.value) * 60000 })} />
              </label>
              <label className='grid gap-1 text-xs text-muted-foreground'>
                防抖（秒）
                <Input type='number' min={0} disabled={!writable} value={Math.round(draft.debounceMs / 1000)} onChange={(event) => setDraft({ ...draft, debounceMs: Number(event.target.value) * 1000 })} />
              </label>
            </div>
            <Textarea className='min-h-24 font-mono text-sm' disabled={!writable} value={draft.expression} onChange={(event) => setDraft({ ...draft, expression: event.target.value })} placeholder='spindleLoad > 90' />
            <div className='grid gap-2'>
              {actions.map((action, index) => (
                <div key={index} className='grid gap-2 rounded-md border p-3 sm:grid-cols-3'>
                  <Select value={action.type} disabled={!writable} onValueChange={(type) => setActions(actions.map((item, i) => i === index ? { ...item, type } : item))}>
                    <SelectTrigger><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem value='alarm'>产生报警</SelectItem>
                      <SelectItem value='notify'>触发通知通道</SelectItem>
                      <SelectItem value='write'>写入计算值</SelectItem>
                      <SelectItem value='event'>北向事件</SelectItem>
                      <SelectItem value='reason'>自动填写停机原因</SelectItem>
                    </SelectContent>
                  </Select>
                  {action.type === 'alarm' ? (
                    <>
                      <Select value={action.severity || 'warning'} disabled={!writable} onValueChange={(severity) => setActions(actions.map((item, i) => i === index ? { ...item, severity } : item))}>
                        <SelectTrigger><SelectValue /></SelectTrigger>
                        <SelectContent>
                          <SelectItem value='info'>提示</SelectItem>
                          <SelectItem value='warning'>警告</SelectItem>
                          <SelectItem value='error'>故障</SelectItem>
                        </SelectContent>
                      </Select>
                      <Input placeholder='报警码' disabled={!writable} value={action.code} onChange={(event) => setActions(actions.map((item, i) => i === index ? { ...item, code: event.target.value } : item))} />
                      <Input className='sm:col-span-3' placeholder='报警说明' disabled={!writable} value={action.message} onChange={(event) => setActions(actions.map((item, i) => i === index ? { ...item, message: event.target.value } : item))} />
                    </>
                  ) : null}
                  {action.type === 'notify' ? <Input placeholder='通知通道 Id' disabled={!writable} value={action.channelId} onChange={(event) => setActions(actions.map((item, i) => i === index ? { ...item, channelId: event.target.value } : item))} /> : null}
                  {action.type === 'write' ? (
                    <>
                      <Input placeholder='写入点位' disabled={!writable} value={action.pointId} onChange={(event) => setActions(actions.map((item, i) => i === index ? { ...item, pointId: event.target.value } : item))} />
                      <Input type='number' placeholder='数值' disabled={!writable} value={action.value ?? ''} onChange={(event) => setActions(actions.map((item, i) => i === index ? { ...item, value: Number(event.target.value) } : item))} />
                    </>
                  ) : null}
                  {action.type === 'event' ? (
                    <>
                      <Input placeholder='事件名' disabled={!writable} value={action.name} onChange={(event) => setActions(actions.map((item, i) => i === index ? { ...item, name: event.target.value } : item))} />
                      <Input placeholder='说明' disabled={!writable} value={action.message} onChange={(event) => setActions(actions.map((item, i) => i === index ? { ...item, message: event.target.value } : item))} />
                    </>
                  ) : null}
                  {action.type === 'reason' ? <Input placeholder='原因编码，如 TOOL' disabled={!writable} value={action.reasonCode} onChange={(event) => setActions(actions.map((item, i) => i === index ? { ...item, reasonCode: event.target.value } : item))} /> : null}
                  <Button variant='ghost' size='sm' disabled={!writable} onClick={() => setActions(actions.filter((_, i) => i !== index))}>移除</Button>
                </div>
              ))}
              <Button variant='outline' size='sm' className='w-fit' disabled={!writable} onClick={() => setActions([...actions, emptyAction()])}>添加动作</Button>
            </div>
            <div className='flex flex-wrap gap-2'>
              <Button disabled={!writable || !licensed} onClick={() => void save()}>保存</Button>
              <Button variant='ghost' disabled={!writable} onClick={() => { setDraft(blank()); setActions([emptyAction()]) }}>新建</Button>
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardHeader><CardTitle>回测</CardTitle></CardHeader>
          <CardContent className='grid gap-3'>
            <p className='text-sm text-muted-foreground'>用这台设备最近约 6 小时的历史，按持续时间和防抖重放条件。</p>
            <Select value={backtestDevice || 'none'} onValueChange={(value) => setBacktestDevice(value === 'none' ? '' : value)}>
              <SelectTrigger><SelectValue placeholder='选择设备' /></SelectTrigger>
              <SelectContent>
                <SelectItem value='none'>选择设备</SelectItem>
                {devices.map((device) => (
                  <SelectItem key={device.metadata.id} value={device.metadata.id}>{device.metadata.displayName || device.metadata.id}</SelectItem>
                ))}
              </SelectContent>
            </Select>
            <Button variant='outline' disabled={!licensed} onClick={() => void backtest()}>按最近历史回测</Button>
            {backtestSummary ? <p className='text-sm' data-testid='rule-backtest'>{backtestSummary}</p> : null}
            <ul className='grid gap-1 text-sm'>
              {hits.map((hit) => (
                <li key={hit.unixMs}>{formatTime(hit.unixMs)} 触发</li>
              ))}
            </ul>
          </CardContent>
        </Card>
      </div>
      <div className='mt-4 grid gap-4 lg:grid-cols-2'>
        <Card>
          <CardHeader><CardTitle>已保存</CardTitle></CardHeader>
          <CardContent>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>名称</TableHead>
                  <TableHead>条件</TableHead>
                  <TableHead />
                </TableRow>
              </TableHeader>
              <TableBody>
                {rules.length === 0 ? <TableRow><TableCell colSpan={3} className='text-muted-foreground'>还没有规则</TableCell></TableRow> : rules.map((rule) => (
                  <TableRow key={rule.id}>
                    <TableCell>{rule.name}{rule.enabled ? '' : '（停用）'}</TableCell>
                    <TableCell className='max-w-[12rem] truncate font-mono text-xs'>{rule.expression}</TableCell>
                    <TableCell className='text-right'>
                      <Button variant='ghost' size='sm' onClick={() => edit(rule)}>编辑</Button>
                      <Button variant='ghost' size='sm' disabled={!writable || !licensed} onClick={() => void remove(rule.id)}>删除</Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
        <Card>
          <CardHeader><CardTitle>执行记录</CardTitle></CardHeader>
          <CardContent>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>时间</TableHead>
                  <TableHead>设备</TableHead>
                  <TableHead>说明</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {logs.length === 0 ? <TableRow><TableCell colSpan={3} className='text-muted-foreground'>还没有触发记录</TableCell></TableRow> : logs.map((row) => (
                  <TableRow key={row.id}>
                    <TableCell>{formatTime(row.unixMs)}</TableCell>
                    <TableCell>{row.deviceId}</TableCell>
                    <TableCell>{row.message}</TableCell>
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
