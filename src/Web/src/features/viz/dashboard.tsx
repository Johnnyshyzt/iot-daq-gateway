import { Link } from '@tanstack/react-router'
import { Maximize2, Radio } from 'lucide-react'
import { ConfigDrawer } from '@/components/config-drawer'
import { Header } from '@/components/layout/header'
import { ProfileDropdown } from '@/components/profile-dropdown'
import { Search } from '@/components/search'
import { ThemeSwitch } from '@/components/theme-switch'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { formatNumber, formatPercent, toggleFullscreen } from './format'
import { stateOrder, stateStyle } from './state-style'
import type { DashboardDevice, MachineState } from './types'
import { OnboardingBanner } from '@/features/daq/onboarding'
import { useLicense } from '@/features/daq/license-banner'
import { useOverview } from './use-overview'

export function DashboardPage({ kiosk = false }: { kiosk?: boolean }) {
  const { data, error, push } = useOverview()
  const license = useLicense()
  const counts = data?.counts

  const body = (
    <div className={cn('flex flex-1 flex-col', kiosk && 'board-screen min-h-svh text-slate-100')}>
      <header className={cn('flex flex-wrap items-end justify-between gap-3 px-4 py-4', kiosk && 'px-6')}>
        <div>
          <p className={cn('text-xs tracking-[0.2em] uppercase', kiosk ? 'text-sky-300/80' : 'text-muted-foreground')}>
            总览大屏
          </p>
          <h1 className='text-2xl font-bold tracking-tight'>设备状态</h1>
          {license?.demoMode ? (
            <p className={cn('mt-1 text-xs font-medium', kiosk ? 'text-amber-200' : 'text-amber-700')}>演示数据 · 模拟器，不是真实机床</p>
          ) : null}
        </div>
        <div className='flex flex-wrap items-center gap-2'>
          <Badge variant='outline' className={cn(push ? 'border-emerald-500/50' : '', kiosk && 'border-slate-600 text-slate-200')}>
            <Radio className='size-3' />
            {push ? '实时推送' : '轮询'}
          </Badge>
          {kiosk ? (
            <Button variant='outline' className='border-slate-600 bg-transparent text-slate-100' onClick={() => void toggleFullscreen()}>
              <Maximize2 />
              全屏
            </Button>
          ) : (
            <Button asChild variant='outline'>
              <Link to='/board'>
                <Maximize2 />
                大屏
              </Link>
            </Button>
          )}
          {kiosk ? (
            <Button asChild variant='outline' className='border-slate-600 bg-transparent text-slate-100'>
              <Link to='/'>返回</Link>
            </Button>
          ) : null}
        </div>
      </header>
      <div className={cn('grid gap-3 px-4 pb-4', kiosk ? 'px-6 sm:grid-cols-4 xl:grid-cols-8' : 'sm:grid-cols-2 xl:grid-cols-4')}>
        {stateOrder.map((state) => (
          <Kpi key={state} kiosk={kiosk} label={stateStyle[state].label} value={counts?.[state] ?? 0} state={state} />
        ))}
        <Kpi kiosk={kiosk} label='今日稼动率' value={formatPercent(data?.todayUtilization ?? 0)} />
        <Kpi kiosk={kiosk} label='今日产量' value={formatNumber(data?.todayPartCount ?? 0, 0)} />
        <Kpi kiosk={kiosk} label='活动报警' value={data?.activeAlarmCount ?? 0} />
      </div>
      {error ? <p className='px-4 pb-2 text-sm text-destructive'>{error}</p> : null}
      <div className={cn('flex flex-1 flex-col gap-5 px-4 pb-6', kiosk && 'px-6')}>
        {!data && !error ? (
          <p className={cn('text-sm', kiosk ? 'text-slate-400' : 'text-muted-foreground')}>正在加载设备状态…</p>
        ) : (data?.groups.length ?? 0) === 0 ? (
          <p className={cn('text-sm', kiosk ? 'text-slate-400' : 'text-muted-foreground')}>
            还没有设备。发布模拟器后，这里会按车间和产线铺开。
          </p>
        ) : null}
        {data?.groups.map((group) => (
          <section key={group.workshop}>
            <h2 className='mb-2 text-sm font-semibold tracking-wide'>{group.workshop}</h2>
            <div className='grid gap-4'>
              {group.lines.map((line) => (
                <div key={line.line}>
                  <p className={cn('mb-2 text-xs', kiosk ? 'text-slate-400' : 'text-muted-foreground')}>{line.line}</p>
                  <div className={cn('grid gap-3', kiosk ? 'sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 2xl:grid-cols-6' : 'sm:grid-cols-2 xl:grid-cols-3')}>
                    {line.devices.map((device) => (
                      <DeviceTile key={device.id} device={device} kiosk={kiosk} />
                    ))}
                  </div>
                </div>
              ))}
            </div>
          </section>
        ))}
      </div>
    </div>
  )

  if (kiosk) return body
  return (
    <>
      <Header>
        <Search className='me-auto' placeholder='搜索' />
        <ThemeSwitch />
        <ConfigDrawer />
        <ProfileDropdown />
      </Header>
      <OnboardingBanner />
      {body}
    </>
  )
}

function Kpi({
  label,
  value,
  state,
  kiosk,
}: {
  label: string
  value: string | number
  state?: MachineState
  kiosk: boolean
}) {
  return (
    <div className={cn('rounded-xl border px-3 py-3', kiosk ? 'border-slate-700 bg-slate-950/70' : 'bg-card')}>
      <div className='flex items-center gap-2 text-xs text-muted-foreground'>
        {state ? <span className={cn('size-2 rounded-full', stateStyle[state].bar)} /> : null}
        <span className={kiosk ? 'text-slate-400' : ''}>{label}</span>
      </div>
      <div className='mt-1 text-2xl font-semibold tabular-nums'>{value}</div>
    </div>
  )
}

function DeviceTile({ device, kiosk }: { device: DashboardDevice; kiosk: boolean }) {
  const style = stateStyle[device.state] ?? stateStyle.idle
  return (
    <Link
      to='/monitor/$deviceId'
      params={{ deviceId: device.id }}
      className={cn(
        'block rounded-xl border border-s-4 p-3 transition-colors hover:bg-accent/40',
        style.tile,
        kiosk && 'border-slate-700 bg-slate-950/80 hover:bg-slate-900'
      )}
    >
      <div className='flex items-start justify-between gap-2'>
        <div className='min-w-0'>
          <div className='truncate font-medium'>
            {device.displayName}
            {device.displayName.includes('演示数据') ? (
              <span className='ms-2 rounded bg-amber-500/15 px-1.5 py-0.5 text-[10px] font-medium text-amber-700'>演示数据</span>
            ) : null}
          </div>
          <div className={cn('truncate font-mono text-xs', kiosk ? 'text-slate-400' : 'text-muted-foreground')}>
            {device.id}
          </div>
        </div>
        <span className={cn('rounded-md px-2 py-0.5 text-xs font-medium', style.chip)}>{device.stateLabel}</span>
      </div>
      <dl className='mt-3 grid grid-cols-2 gap-x-3 gap-y-1 text-xs'>
        <Field kiosk={kiosk} label='程序' value={device.program || '—'} />
        <Field kiosk={kiosk} label='主轴' value={device.spindleSpeed == null ? '—' : `${formatNumber(device.spindleSpeed, 0)} rpm`} />
        <Field kiosk={kiosk} label='今日产量' value={formatNumber(device.partCount, 0)} />
        <Field kiosk={kiosk} label='稼动率' value={formatPercent(device.utilization)} />
      </dl>
      {device.activeAlarms > 0 ? (
        <p className='mt-2 text-xs text-red-500'>{device.activeAlarms} 条活动报警</p>
      ) : null}
    </Link>
  )
}

function Field({ label, value, kiosk }: { label: string; value: string; kiosk: boolean }) {
  return (
    <div className='min-w-0'>
      <dt className={cn(kiosk ? 'text-slate-500' : 'text-muted-foreground')}>{label}</dt>
      <dd className='truncate font-medium'>{value}</dd>
    </div>
  )
}
