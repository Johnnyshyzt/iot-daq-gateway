import type { MachineState } from './types'

export const stateOrder: MachineState[] = ['running', 'idle', 'alarm', 'offline', 'stopped']

export const stateStyle: Record<
  MachineState,
  { label: string; bar: string; chip: string; tile: string }
> = {
  running: {
    label: '运行',
    bar: 'bg-emerald-500',
    chip: 'bg-emerald-500/15 text-emerald-700 dark:text-emerald-300',
    tile: 'border-emerald-500/80',
  },
  idle: {
    label: '待机',
    bar: 'bg-amber-400',
    chip: 'bg-amber-400/20 text-amber-800 dark:text-amber-200',
    tile: 'border-amber-400/80',
  },
  alarm: {
    label: '报警',
    bar: 'bg-red-500',
    chip: 'bg-red-500/15 text-red-700 dark:text-red-300',
    tile: 'border-red-500/80',
  },
  offline: {
    label: '离线',
    bar: 'bg-slate-400',
    chip: 'bg-slate-400/20 text-slate-700 dark:text-slate-200',
    tile: 'border-slate-400/80',
  },
  stopped: {
    label: '停机',
    bar: 'bg-orange-500',
    chip: 'bg-orange-500/15 text-orange-800 dark:text-orange-200',
    tile: 'border-orange-500/80',
  },
}
