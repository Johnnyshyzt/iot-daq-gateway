import { useEffect, useState } from 'react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { canWrite, describeError, studioApi } from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'
import type { VizSettings } from './types'

export function VizSettingsCard() {
  const writable = canWrite(useAuthStore((state) => state.auth.user?.role[0]))
  const [settings, setSettings] = useState<VizSettings | null>(null)
  const [message, setMessage] = useState('')

  useEffect(() => {
    void studioApi<VizSettings>('/api/v1/viz/settings')
      .then(setSettings)
      .catch((error: unknown) => setMessage(describeError(error)))
  }, [])

  async function save() {
    if (!settings) return
    const next = await studioApi<VizSettings>('/api/v1/viz/settings', {
      method: 'PUT',
      body: JSON.stringify(settings),
    })
    setSettings(next)
    setMessage('班次和历史保留已生效')
    toast.success('可视化设置已保存')
  }

  if (!settings) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>班次与历史保留</CardTitle>
        </CardHeader>
        <CardContent className='text-sm text-muted-foreground'>{message || '加载中…'}</CardContent>
      </Card>
    )
  }

  return (
    <Card className='lg:col-span-2'>
      <CardHeader>
        <CardTitle>班次与历史保留</CardTitle>
      </CardHeader>
      <CardContent className='grid gap-3'>
        <div className='grid gap-3 sm:grid-cols-2'>
          <Field label='历史保留（天）'>
            <Input
              type='number'
              min={1}
              max={3650}
              disabled={!writable}
              value={settings.historyRetentionDays}
              onChange={(event) => setSettings({ ...settings, historyRetentionDays: Number(event.target.value) })}
            />
          </Field>
          <Field label='时区'>
            <Input
              disabled={!writable}
              value={settings.timeZone}
              onChange={(event) => setSettings({ ...settings, timeZone: event.target.value })}
            />
          </Field>
        </div>
        {settings.shifts.map((shift, index) => (
          <div key={`${shift.name}-${index}`} className='grid gap-2 sm:grid-cols-4'>
            <Input
              disabled={!writable}
              value={shift.name}
              onChange={(event) => updateShift(index, { name: event.target.value })}
            />
            <Input
              disabled={!writable}
              value={shift.start}
              onChange={(event) => updateShift(index, { start: event.target.value })}
            />
            <Input
              disabled={!writable}
              value={shift.end}
              onChange={(event) => updateShift(index, { end: event.target.value })}
            />
            <Input
              type='number'
              disabled={!writable}
              value={shift.plannedMinutes}
              onChange={(event) => updateShift(index, { plannedMinutes: Number(event.target.value) })}
            />
          </div>
        ))}
        <div className='flex flex-wrap gap-2'>
          <Button
            variant='outline'
            disabled={!writable}
            onClick={() =>
              setSettings({
                ...settings,
                shifts: [...settings.shifts, { name: '新班次', start: '08:00', end: '16:00', plannedMinutes: 420 }],
              })
            }
          >
            增加班次
          </Button>
          <Button disabled={!writable} onClick={() => void save().catch((error: unknown) => setMessage(describeError(error)))}>
            保存班次
          </Button>
        </div>
        <p className='text-xs text-muted-foreground'>时间用 HH:mm。计划分钟不能超过班次长度，班次之间不能重叠。后台按这个天数清理历史、已恢复报警和已结束的状态记录。</p>
        {message ? <p className='text-sm text-muted-foreground'>{message}</p> : null}
      </CardContent>
    </Card>
  )

  function updateShift(index: number, patch: Partial<VizSettings['shifts'][number]>) {
    setSettings((current) => {
      if (!current) return current
      const shifts = current.shifts.map((shift, item) => (item === index ? { ...shift, ...patch } : shift))
      return { ...current, shifts }
    })
  }
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className='grid gap-1.5'>
      <Label>{label}</Label>
      {children}
    </div>
  )
}
