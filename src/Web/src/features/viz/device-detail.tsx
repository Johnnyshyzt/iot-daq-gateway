import { useEffect, useState } from 'react'
import { Link } from '@tanstack/react-router'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { describeError, studioApi } from '@/lib/studio-api'
import { formatClock, formatNumber, formatPercent, formatTime } from './format'
import { PageShell } from '@/features/daq/page-shell'
import { stateStyle } from './state-style'
import type { DeviceDetail, TimelinePiece } from './types'

export function DeviceDetailPage({ deviceId }: { deviceId: string }) {
  const [detail, setDetail] = useState<DeviceDetail | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    let stop = false
    async function load() {
      try {
        const next = await studioApi<DeviceDetail>(`/api/v1/devices/${encodeURIComponent(deviceId)}/detail`)
        if (!stop) {
          setDetail(next)
          setError('')
        }
      } catch (err) {
        if (!stop) setError(describeError(err))
      }
    }
    void load()
    const timer = window.setInterval(() => void load(), 2000)
    return () => {
      stop = true
      window.clearInterval(timer)
    }
  }, [deviceId])

  const style = stateStyle[detail?.state ?? 'idle']
  return (
    <PageShell
      title={detail?.displayName || deviceId}
      description={detail ? `${detail.workshop} · ${detail.line} · ${detail.adapter}` : '单机详情'}
      actions={
        <Button asChild variant='outline'>
          <Link to='/history' search={{ device: deviceId }}>
            历史曲线
          </Link>
        </Button>
      }
    >
      {error ? <p className='mb-3 text-sm text-destructive'>{error}</p> : null}
      {!detail ? <p className='text-sm text-muted-foreground'>加载中…</p> : null}
      {detail ? (
        <div className='grid gap-4'>
          <Card>
            <CardHeader className='flex flex-row items-center justify-between'>
              <CardTitle className='flex items-center gap-2'>
                今日状态
                <span className={`rounded-md px-2 py-0.5 text-xs ${style.chip}`}>{detail.stateLabel}</span>
              </CardTitle>
              <span className='text-xs text-muted-foreground'>
                {formatClock(detail.timelineFromUnixMs)} – {formatClock(detail.timelineToUnixMs)}
              </span>
            </CardHeader>
            <CardContent>
              <StateTimeline pieces={detail.timeline} from={detail.timelineFromUnixMs} to={detail.timelineToUnixMs} />
            </CardContent>
          </Card>
          <div className='grid gap-3 sm:grid-cols-2 xl:grid-cols-4'>
            <Gauge label='主轴转速' value={detail.spindleSpeed} unit='rpm' max={3000} />
            <Gauge label='主轴负载' value={detail.spindleLoad} unit='%' max={100} />
            <Gauge label='进给' value={detail.feedRate} unit='mm/min' max={2000} />
            <Gauge label='进给倍率' value={detail.feedOverride} unit='%' max={150} />
          </div>
          <div className='grid gap-4 lg:grid-cols-3'>
            <Card>
              <CardHeader>
                <CardTitle>程序与坐标</CardTitle>
              </CardHeader>
              <CardContent className='space-y-2 text-sm'>
                <Row label='程序' value={detail.program || '—'} />
                <Row label='行号' value={formatNumber(detail.programLine, 0)} />
                <Row label='模式' value={detail.workMode || '—'} />
                <Row label='X' value={axis(detail.axisX)} />
                <Row label='Y' value={axis(detail.axisY)} />
                <Row label='Z' value={axis(detail.axisZ)} />
                <Row label='件数' value={formatNumber(detail.partCount, 0)} />
                <Row label='累计' value={formatNumber(detail.partCountTotal, 0)} />
                <Row label='主轴倍率' value={detail.spindleOverride == null ? '—' : `${formatNumber(detail.spindleOverride, 0)}%`} />
                <Row label='快速倍率' value={detail.rapidOverride == null ? '—' : `${formatNumber(detail.rapidOverride, 0)}%`} />
              </CardContent>
            </Card>
            <Card>
              <CardHeader>
                <CardTitle>今日</CardTitle>
              </CardHeader>
              <CardContent className='space-y-2 text-sm'>
                <Row label='稼动率' value={formatPercent(detail.todayUtilization)} />
                <Row label='产量' value={formatNumber(detail.todayPartCount, 0)} />
                <Row label='连接' value={connectionLabel(detail.connection)} />
                <Row label='链路' value={linkText(detail.linkPhase, detail.linkAttempt)} />
                <Row label='驱动' value={detail.adapter} />
                <Row label='地址' value={detail.host ? `${detail.host}:${detail.port}` : '—'} />
                <p className='text-xs text-muted-foreground'>{detail.connectionMessage || '等待采集状态'}</p>
                {detail.linkPhase === 'backoff' && detail.nextRetryUnixMs ? (
                  <p className='text-xs text-muted-foreground'>下次重试 {formatTime(detail.nextRetryUnixMs)}</p>
                ) : null}
                {detail.lastError ? <p className='text-xs text-destructive'>{detail.lastError}</p> : null}
                {detail.lastSeenUnixMs ? (
                  <p className='text-xs text-muted-foreground'>最近通讯 {formatTime(detail.lastSeenUnixMs)}</p>
                ) : null}
              </CardContent>
            </Card>
            <Card>
              <CardHeader>
                <CardTitle>活动报警</CardTitle>
              </CardHeader>
              <CardContent className='space-y-2 text-sm'>
                {detail.activeAlarms.length === 0 ? <p className='text-muted-foreground'>当前没有活动报警。</p> : null}
                {detail.activeAlarms.map((alarm) => (
                  <div key={alarm.id} className='rounded-md border px-2 py-1.5'>
                    <div className='font-medium'>{alarm.code || alarm.pointId}</div>
                    <div className='text-muted-foreground'>{alarm.message}</div>
                  </div>
                ))}
                {detail.alarmText && detail.alarmText !== '0' ? (
                  <p className='text-xs text-muted-foreground'>当前报警文本：{detail.alarmText}</p>
                ) : null}
              </CardContent>
            </Card>
          </div>
          <Card>
            <CardHeader>
              <CardTitle>最近事件</CardTitle>
            </CardHeader>
            <CardContent>
              {detail.recentEvents.length === 0 ? (
                <p className='text-sm text-muted-foreground'>今天还没有状态或报警事件。</p>
              ) : (
                <ul className='space-y-1 text-sm'>
                  {detail.recentEvents.map((event, index) => (
                    <li key={`${event.timestampUnixMs}-${index}`} className='flex gap-3'>
                      <span className='w-28 shrink-0 text-xs text-muted-foreground'>{formatTime(event.timestampUnixMs)}</span>
                      <Badge variant='secondary'>{event.kind === 'alarm' ? '报警' : '状态'}</Badge>
                      <span>{event.message}</span>
                    </li>
                  ))}
                </ul>
              )}
            </CardContent>
          </Card>
        </div>
      ) : null}
    </PageShell>
  )
}

function StateTimeline({ pieces, from, to }: { pieces: TimelinePiece[]; from: number; to: number }) {
  const span = Math.max(1, to - from)
  return (
    <div>
      <div className='flex h-8 overflow-hidden rounded-md bg-muted'>
        {pieces.map((piece) => (
          <div
            key={`${piece.startedUnixMs}-${piece.state}`}
            title={`${piece.label} ${formatClock(piece.startedUnixMs)}–${formatClock(piece.endedUnixMs)}`}
            className={stateStyle[piece.state]?.bar ?? 'bg-slate-400'}
            style={{ width: `${((piece.endedUnixMs - piece.startedUnixMs) / span) * 100}%` }}
          />
        ))}
      </div>
      <div className='mt-2 flex flex-wrap gap-3 text-xs text-muted-foreground'>
        {Object.entries(stateStyle).map(([key, style]) => (
          <span key={key} className='inline-flex items-center gap-1'>
            <span className={`size-2 rounded-full ${style.bar}`} />
            {style.label}
          </span>
        ))}
      </div>
    </div>
  )
}

function Gauge({ label, value, unit, max }: { label: string; value?: number | null; unit: string; max: number }) {
  const number = value ?? 0
  const pct = value == null ? 0 : Math.max(0, Math.min(1, number / max))
  const angle = -210 + pct * 240
  return (
    <Card>
      <CardContent className='flex items-center gap-3 pt-6'>
        <svg viewBox='0 0 36 36' className='size-16 shrink-0'>
          <path d='M6 28 A14 14 0 1 1 30 28' fill='none' className='stroke-muted' strokeWidth='3' />
          <path
            d='M6 28 A14 14 0 1 1 30 28'
            fill='none'
            className='stroke-primary'
            strokeWidth='3'
            strokeDasharray={`${pct * 66} 66`}
            strokeLinecap='round'
          />
          <line x1='18' y1='18' x2='18' y2='8' className='stroke-foreground' strokeWidth='1.2' transform={`rotate(${angle} 18 18)`} />
        </svg>
        <div>
          <div className='text-xs text-muted-foreground'>{label}</div>
          <div className='text-xl font-semibold tabular-nums'>
            {value == null ? '—' : formatNumber(value, unit === '%' ? 0 : 1)}
            <span className='ms-1 text-xs font-normal text-muted-foreground'>{value == null ? '' : unit}</span>
          </div>
        </div>
      </CardContent>
    </Card>
  )
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className='flex justify-between gap-3'>
      <span className='text-muted-foreground'>{label}</span>
      <span className='truncate font-medium'>{value}</span>
    </div>
  )
}

function axis(value?: number | null) {
  return value == null ? '—' : `${formatNumber(value, 3)} mm`
}

function connectionLabel(status: string) {
  if (status === 'online') return '在线'
  if (status === 'offline') return '离线'
  if (status === 'degraded') return '降级'
  if (status === 'disabled') return '已停用'
  return status || '—'
}

function linkText(phase?: string, attempt?: number) {
  const name = phase === 'connected' ? '已连接' : phase === 'connecting' ? '连接中' : phase === 'backoff' ? '退避重连' : '—'
  return attempt ? `${name} · 第 ${attempt} 次` : name
}
