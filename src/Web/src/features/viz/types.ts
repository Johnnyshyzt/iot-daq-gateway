export type MachineState = 'running' | 'idle' | 'alarm' | 'offline' | 'stopped'

export type DashboardDevice = {
  id: string
  displayName: string
  workshop: string
  line: string
  adapter: string
  state: MachineState
  stateLabel: string
  program?: string | null
  partCount: number
  utilization: number
  spindleSpeed?: number | null
  spindleLoad?: number | null
  feedRate?: number | null
  activeAlarms: number
  connection: string
  connectionMessage: string
}

export type DashboardSnapshot = {
  generatedUnixMs: number
  counts: Record<MachineState, number>
  todayUtilization: number
  todayPartCount: number
  activeAlarmCount: number
  groups: Array<{
    workshop: string
    lines: Array<{ line: string; devices: DashboardDevice[] }>
  }>
}

export type TimelinePiece = {
  state: MachineState
  label: string
  startedUnixMs: number
  endedUnixMs: number
}

export type AlarmItem = {
  id: string
  deviceId: string
  pointId: string
  code: string
  message: string
  severity: string
  active: boolean
  raisedUnixMs: number
  clearedUnixMs?: number | null
  durationMs?: number | null
  acknowledged: boolean
  acknowledgedBy?: string | null
}

export type DeviceDetail = {
  id: string
  displayName: string
  workshop: string
  line: string
  adapter: string
  enabled: boolean
  host: string
  port: number
  state: MachineState
  stateLabel: string
  connection: string
  connectionMessage: string
  linkPhase?: string
  nextRetryUnixMs?: number | null
  lastError?: string
  linkAttempt?: number
  lastSeenUnixMs?: number | null
  program?: string | null
  programLine?: number | null
  workMode?: string | null
  partCount?: number | null
  partCountTotal?: number | null
  spindleSpeed?: number | null
  spindleLoad?: number | null
  spindleOverride?: number | null
  feedRate?: number | null
  feedOverride?: number | null
  rapidOverride?: number | null
  axisX?: number | null
  axisY?: number | null
  axisZ?: number | null
  alarmText?: string | null
  todayUtilization: number
  todayPartCount: number
  timelineFromUnixMs: number
  timelineToUnixMs: number
  timeline: TimelinePiece[]
  activeAlarms: AlarmItem[]
  recentEvents: Array<{ timestampUnixMs: number; kind: string; message: string }>
}

export type SeriesPoint = {
  deviceId: string
  pointId: string
  timestampUnixMs: number
  avg: number
  min: number
  max: number
  count: number
}

export type SeriesResponse = {
  bucketMs: number
  fromUnixMs: number
  toUnixMs: number
  series: SeriesPoint[]
}

export type UtilizationRow = {
  deviceId: string
  displayName: string
  workshop: string
  line: string
  day: string
  shift: string
  runMs: number
  idleMs: number
  alarmMs: number
  offlineMs: number
  stoppedMs: number
  plannedMs: number
  utilization: number
  partCount: number
}

export type UtilizationResponse = {
  fromUnixMs: number
  toUnixMs: number
  shifts: UtilizationRow[]
  days: UtilizationRow[]
  lines: UtilizationRow[]
}

export type VizSettings = {
  historyRetentionDays: number
  timeZone: string
  shifts: Array<{ name: string; start: string; end: string; plannedMinutes: number }>
}

export type AlarmStat = { key: string; label: string; count: number; durationMs: number }
