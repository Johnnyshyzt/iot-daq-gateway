import { useEffect, useState } from 'react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { canWrite, describeError, studioApi } from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'
import { FeatureLock } from './license-banner'
import { PageShell } from './page-shell'

type Channel = {
  id: string
  name: string
  kind: string
  enabled: boolean
  webhookUrl: string
  hasSecret: boolean
  secretFromEnv: string
  smtpHost: string
  smtpPort: number
  smtpUser: string
  mailFrom: string
  mailTo: string
  smtpSsl: boolean
}

type Rule = {
  id: string
  name: string
  enabled: boolean
  channelId: string
  escalationChannelId: string
  escalationMinutes: number
  deviceIds: string[]
  groups: string[]
  severities: string[]
  codeFilter: string
  onRaise: boolean
  onClear: boolean
  quietStart: string
  quietEnd: string
  dedupSeconds: number
  ratePerHour: number
}

type Schedule = {
  enabled: boolean
  dailyEnabled: boolean
  dailyTime: string
  shiftEnabled: boolean
  channelIds: string[]
  lastDailyKey: string
  lastShiftKey: string
}

type Preview = {
  title: string
  text: string
  lines: Array<{ deviceId: string; displayName: string; utilization: number; partCount: number; topAlarms: string[] }>
}

const kinds = [
  { value: 'wecom', label: '企业微信群机器人' },
  { value: 'dingtalk', label: '钉钉机器人' },
  { value: 'feishu', label: '飞书 / Lark' },
  { value: 'smtp', label: '邮件 SMTP' },
  { value: 'webhook', label: '通用 Webhook' },
]

const emptyChannel = (): Channel & { secret: string } => ({
  id: '',
  name: '',
  kind: 'wecom',
  enabled: true,
  webhookUrl: '',
  hasSecret: false,
  secret: '',
  secretFromEnv: '',
  smtpHost: '',
  smtpPort: 587,
  smtpUser: '',
  mailFrom: '',
  mailTo: '',
  smtpSsl: true,
})

export function NotifyPage() {
  const writable = canWrite(useAuthStore((state) => state.auth.user?.role[0]))
  const [channels, setChannels] = useState<Channel[]>([])
  const [rules, setRules] = useState<Rule[]>([])
  const [channel, setChannel] = useState(emptyChannel())
  const [rule, setRule] = useState<Rule>({
    id: '',
    name: '',
    enabled: true,
    channelId: '',
    escalationChannelId: '',
    escalationMinutes: 0,
    deviceIds: [],
    groups: [],
    severities: [],
    codeFilter: '',
    onRaise: true,
    onClear: false,
    quietStart: '',
    quietEnd: '',
    dedupSeconds: 300,
    ratePerHour: 30,
  })
  const [schedule, setSchedule] = useState<Schedule | null>(null)
  const [preview, setPreview] = useState<Preview | null>(null)
  const [message, setMessage] = useState('')

  async function reload() {
    const [channelBody, ruleBody, report] = await Promise.all([
      studioApi<{ channels: Channel[] }>('/api/v1/notifications/channels'),
      studioApi<{ rules: Rule[] }>('/api/v1/notifications/rules'),
      studioApi<Schedule>('/api/v1/notifications/reports'),
    ])
    setChannels(channelBody.channels)
    setRules(ruleBody.rules)
    setSchedule(report)
  }

  useEffect(() => {
    void reload().catch((error: unknown) => setMessage(describeError(error)))
  }, [])

  function fail(error: unknown) {
    const text = describeError(error)
    setMessage(text)
    toast.error(text)
  }

  async function saveChannel() {
    const saved = await studioApi<Channel>('/api/v1/notifications/channels', {
      method: 'PUT',
      body: JSON.stringify({
        ...channel,
        secret: channel.secret,
        deviceIds: undefined,
      }),
    })
    setChannel({ ...emptyChannel(), ...saved, secret: '' })
    await reload()
    toast.success('通知通道已保存')
  }

  async function testChannel(id: string) {
    const result = await studioApi<{ status: string; lastError: string }>(`/api/v1/notifications/channels/${id}/test`, {
      method: 'POST',
    })
    toast.success(result.status === 'sent' ? '测试消息已发送' : `测试未成功：${result.lastError || result.status}`)
  }

  async function removeChannel(id: string) {
    await studioApi(`/api/v1/notifications/channels/${id}`, { method: 'DELETE' })
    await reload()
  }

  async function saveRule() {
    await studioApi('/api/v1/notifications/rules', {
      method: 'PUT',
      body: JSON.stringify({
        ...rule,
        deviceIds: split(rule.deviceIds.join(',')),
        groups: split(rule.groups.join(',')),
        severities: split(rule.severities.join(',')),
      }),
    })
    setRule({ ...rule, id: '', name: '' })
    await reload()
    toast.success('通知规则已保存')
  }

  async function removeRule(id: string) {
    await studioApi(`/api/v1/notifications/rules/${id}`, { method: 'DELETE' })
    await reload()
  }

  async function saveSchedule() {
    if (!schedule) return
    const saved = await studioApi<Schedule>('/api/v1/notifications/reports', {
      method: 'PUT',
      body: JSON.stringify(schedule),
    })
    setSchedule(saved)
    toast.success('报表计划已保存')
  }

  async function loadPreview(cadence: string) {
    const next = await studioApi<Preview>(`/api/v1/notifications/reports/preview?cadence=${cadence}`)
    setPreview(next)
  }

  return (
    <PageShell title='通知' description='报警发到企业微信、钉钉、飞书、邮件或 Webhook。密钥只写入，接口不回传明文。'>
      <FeatureLock feature='alarm-notifications' />
      {message ? <p className='mb-3 text-sm text-destructive'>{message}</p> : null}
      <div className='grid gap-4 xl:grid-cols-2'>
        <Card>
          <CardHeader>
            <CardTitle>通道</CardTitle>
          </CardHeader>
          <CardContent className='grid gap-3'>
            {channels.map((item) => (
              <div key={item.id} className='flex flex-wrap items-center justify-between gap-2 rounded-md border px-3 py-2 text-sm'>
                <div>
                  <div className='font-medium'>{item.name}</div>
                  <div className='text-xs text-muted-foreground'>
                    {kindLabel(item.kind)} · {item.enabled ? '启用' : '停用'} · {item.hasSecret ? '已保存密钥' : '无签名密钥'}
                  </div>
                </div>
                <div className='flex gap-2'>
                  <Button size='sm' variant='outline' disabled={!writable} onClick={() => void testChannel(item.id).catch(fail)}>
                    发送测试
                  </Button>
                  <Button size='sm' variant='outline' disabled={!writable} onClick={() => setChannel({ ...emptyChannel(), ...item, secret: '' })}>
                    编辑
                  </Button>
                  <Button size='sm' variant='outline' disabled={!writable} onClick={() => void removeChannel(item.id).catch(fail)}>
                    删除
                  </Button>
                </div>
              </div>
            ))}
            <Label>名称</Label>
            <Input value={channel.name} disabled={!writable} onChange={(event) => setChannel({ ...channel, name: event.target.value })} />
            <Label>类型</Label>
            <Select value={channel.kind} onValueChange={(kind) => setChannel({ ...channel, kind })} disabled={!writable}>
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {kinds.map((item) => (
                  <SelectItem key={item.value} value={item.value}>
                    {item.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            {channel.kind === 'smtp' ? (
              <>
                <Label>SMTP 主机</Label>
                <Input value={channel.smtpHost} disabled={!writable} onChange={(event) => setChannel({ ...channel, smtpHost: event.target.value })} />
                <Label>端口</Label>
                <Input type='number' value={channel.smtpPort} disabled={!writable} onChange={(event) => setChannel({ ...channel, smtpPort: Number(event.target.value) })} />
                <Label>账号</Label>
                <Input value={channel.smtpUser} disabled={!writable} onChange={(event) => setChannel({ ...channel, smtpUser: event.target.value })} />
                <Label>发件人</Label>
                <Input value={channel.mailFrom} disabled={!writable} onChange={(event) => setChannel({ ...channel, mailFrom: event.target.value })} />
                <Label>收件人</Label>
                <Input value={channel.mailTo} disabled={!writable} onChange={(event) => setChannel({ ...channel, mailTo: event.target.value })} />
              </>
            ) : (
              <>
                <Label>Webhook</Label>
                <Input value={channel.webhookUrl} disabled={!writable} onChange={(event) => setChannel({ ...channel, webhookUrl: event.target.value })} />
              </>
            )}
            <Label>签名密钥或邮箱密码</Label>
            <Input
              type='password'
              value={channel.secret}
              disabled={!writable}
              placeholder={channel.hasSecret ? '留空则保留已保存的密钥' : '可留空'}
              onChange={(event) => setChannel({ ...channel, secret: event.target.value })}
            />
            <Label>密钥环境变量（可选，优先于上面的密钥）</Label>
            <Input value={channel.secretFromEnv} disabled={!writable} onChange={(event) => setChannel({ ...channel, secretFromEnv: event.target.value })} />
            <div className='flex items-center gap-2'>
              <Switch checked={channel.enabled} disabled={!writable} onCheckedChange={(enabled) => setChannel({ ...channel, enabled })} />
              <span className='text-sm'>启用</span>
            </div>
            <Button disabled={!writable} onClick={() => void saveChannel().catch(fail)}>
              保存通道
            </Button>
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>规则</CardTitle>
          </CardHeader>
          <CardContent className='grid gap-3'>
            {rules.map((item) => (
              <div key={item.id} className='flex items-center justify-between gap-2 rounded-md border px-3 py-2 text-sm'>
                <div>
                  <div className='font-medium'>{item.name}</div>
                  <div className='text-xs text-muted-foreground'>
                    {item.onRaise ? '发生' : ''}
                    {item.onClear ? ' 恢复' : ''}
                    {item.escalationMinutes > 0 ? ` · ${item.escalationMinutes} 分钟未确认则升级` : ''}
                    {item.quietStart ? ` · 免打扰 ${item.quietStart}-${item.quietEnd}` : ''}
                  </div>
                </div>
                <div className='flex gap-2'>
                  <Button size='sm' variant='outline' disabled={!writable} onClick={() => setRule(item)}>
                    编辑
                  </Button>
                  <Button size='sm' variant='outline' disabled={!writable} onClick={() => void removeRule(item.id).catch(fail)}>
                    删除
                  </Button>
                </div>
              </div>
            ))}
            <Label>名称</Label>
            <Input value={rule.name} disabled={!writable} onChange={(event) => setRule({ ...rule, name: event.target.value })} />
            <Label>通道</Label>
            <Select value={rule.channelId || undefined} onValueChange={(channelId) => setRule({ ...rule, channelId })} disabled={!writable}>
              <SelectTrigger>
                <SelectValue placeholder='选择通道' />
              </SelectTrigger>
              <SelectContent>
                {channels.map((item) => (
                  <SelectItem key={item.id} value={item.id}>
                    {item.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <Label>设备 Id（逗号分隔，空表示全部）</Label>
            <Input value={rule.deviceIds.join(',')} disabled={!writable} onChange={(event) => setRule({ ...rule, deviceIds: split(event.target.value) })} />
            <Label>车间或产线（逗号分隔，空表示全部）</Label>
            <Input value={rule.groups.join(',')} disabled={!writable} onChange={(event) => setRule({ ...rule, groups: split(event.target.value) })} />
            <Label>级别（alarm, estop，空表示全部）</Label>
            <Input value={rule.severities.join(',')} disabled={!writable} onChange={(event) => setRule({ ...rule, severities: split(event.target.value) })} />
            <Label>报警代码过滤（支持 * 和逗号）</Label>
            <Input value={rule.codeFilter} disabled={!writable} onChange={(event) => setRule({ ...rule, codeFilter: event.target.value })} />
            <div className='flex gap-4 text-sm'>
              <label className='flex items-center gap-2'>
                <Switch checked={rule.onRaise} disabled={!writable} onCheckedChange={(onRaise) => setRule({ ...rule, onRaise })} />
                发生时
              </label>
              <label className='flex items-center gap-2'>
                <Switch checked={rule.onClear} disabled={!writable} onCheckedChange={(onClear) => setRule({ ...rule, onClear })} />
                恢复时
              </label>
            </div>
            <Label>未确认升级（分钟，0 为关闭）</Label>
            <Input type='number' value={rule.escalationMinutes} disabled={!writable} onChange={(event) => setRule({ ...rule, escalationMinutes: Number(event.target.value) })} />
            <Label>升级通道</Label>
            <Select
              value={rule.escalationChannelId || 'none'}
              onValueChange={(value) => setRule({ ...rule, escalationChannelId: value === 'none' ? '' : value })}
              disabled={!writable}
            >
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value='none'>不升级</SelectItem>
                {channels.map((item) => (
                  <SelectItem key={item.id} value={item.id}>
                    {item.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <div className='grid grid-cols-2 gap-2'>
              <div>
                <Label>免打扰开始</Label>
                <Input value={rule.quietStart} placeholder='22:00' disabled={!writable} onChange={(event) => setRule({ ...rule, quietStart: event.target.value })} />
              </div>
              <div>
                <Label>免打扰结束</Label>
                <Input value={rule.quietEnd} placeholder='07:00' disabled={!writable} onChange={(event) => setRule({ ...rule, quietEnd: event.target.value })} />
              </div>
            </div>
            <div className='grid grid-cols-2 gap-2'>
              <div>
                <Label>去重（秒）</Label>
                <Input type='number' value={rule.dedupSeconds} disabled={!writable} onChange={(event) => setRule({ ...rule, dedupSeconds: Number(event.target.value) })} />
              </div>
              <div>
                <Label>每小时上限</Label>
                <Input type='number' value={rule.ratePerHour} disabled={!writable} onChange={(event) => setRule({ ...rule, ratePerHour: Number(event.target.value) })} />
              </div>
            </div>
            <Button disabled={!writable} onClick={() => void saveRule().catch(fail)}>
              保存规则
            </Button>
          </CardContent>
        </Card>
      </div>
      <Card className='mt-4'>
        <CardHeader>
          <CardTitle>稼动报表</CardTitle>
        </CardHeader>
        <CardContent className='grid gap-3'>
          {schedule ? (
            <>
              <label className='flex items-center gap-2 text-sm'>
                <Switch checked={schedule.enabled} disabled={!writable} onCheckedChange={(enabled) => setSchedule({ ...schedule, enabled })} />
                启用定时发送
              </label>
              <label className='flex items-center gap-2 text-sm'>
                <Switch checked={schedule.dailyEnabled} disabled={!writable} onCheckedChange={(dailyEnabled) => setSchedule({ ...schedule, dailyEnabled })} />
                每天发送前一日汇总
              </label>
              <Label>发送时间（本地时区，HH:mm）</Label>
              <Input className='max-w-40' value={schedule.dailyTime} disabled={!writable} onChange={(event) => setSchedule({ ...schedule, dailyTime: event.target.value })} />
              <label className='flex items-center gap-2 text-sm'>
                <Switch checked={schedule.shiftEnabled} disabled={!writable} onCheckedChange={(shiftEnabled) => setSchedule({ ...schedule, shiftEnabled })} />
                班次结束时发送
              </label>
              <Label>接收通道 Id（逗号分隔）</Label>
              <Input
                value={schedule.channelIds.join(',')}
                disabled={!writable}
                onChange={(event) => setSchedule({ ...schedule, channelIds: split(event.target.value) })}
              />
              <div className='text-xs text-muted-foreground'>
                上次日报 {schedule.lastDailyKey || '尚未发送'} · 上次班次 {schedule.lastShiftKey || '尚未发送'}
              </div>
              <div className='flex flex-wrap gap-2'>
                <Button disabled={!writable} onClick={() => void saveSchedule().catch(fail)}>
                  保存计划
                </Button>
                <Button variant='outline' onClick={() => void loadPreview('daily').catch(fail)}>
                  预览日报
                </Button>
                <Button variant='outline' onClick={() => void loadPreview('shift').catch(fail)}>
                  预览班次
                </Button>
              </div>
            </>
          ) : (
            <p className='text-sm text-muted-foreground'>加载中…</p>
          )}
          {preview ? (
            <pre className='max-h-80 overflow-auto rounded-md border bg-muted/40 p-3 text-xs whitespace-pre-wrap'>{preview.text}</pre>
          ) : null}
        </CardContent>
      </Card>
    </PageShell>
  )
}

function kindLabel(kind: string) {
  return kinds.find((item) => item.value === kind)?.label ?? kind
}

function split(value: string) {
  return value
    .split(/[,，\s]+/)
    .map((item) => item.trim())
    .filter(Boolean)
}
