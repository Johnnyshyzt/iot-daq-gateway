import { useEffect, useMemo, useState } from 'react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
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
import { describeError, isAdmin, studioApi } from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'

type Gateway = {
  id: string
  name: string
  groupId: string
  site: string
  version: string
  licenseSummary: string
  deviceCount: number
  onlineLinks: number
  offlineLinks: number
  workingSetMb: number | null
  status: string
  lastSeenUnixMs: number
  summaryJson: string
}

type Group = { id: string; name: string }
type Alert = { gatewayId: string; message: string; active: boolean; raisedUnixMs: number }
type Template = { key: string; name: string; kind: string; version: number; comment: string; bodyJson: string }
type Push = { id: string; templateKey: string; version: number; previousVersion: number | null; diffText: string; conflictPolicy: string; status: string; rollback: boolean }
type PushTarget = { pushId: string; gatewayId: string; status: string; message: string }
type Rollout = { id: string; version: string; sha256: string; status: string }
type RolloutTarget = { rolloutId: string; gatewayId: string; status: string; message: string }
type DiffLine = { kind: string; text: string }

type FleetView = {
  licensed: boolean
  message?: string
  mode: string
  gateways: Gateway[]
  groups: Group[]
  alerts: Alert[]
}

const statusText: Record<string, string> = {
  online: '在线',
  offline: '离线',
  staged: '已暂存',
  pending: '等待',
  failed: '失败',
  applying: '下发中',
  applied: '已应用',
  rolling: '推送中',
  skipped: '已跳过',
}

export function CentralPage() {
  const admin = isAdmin(useAuthStore((state) => state.auth.user?.role[0]))
  const [fleet, setFleet] = useState<FleetView | null>(null)
  const [templates, setTemplates] = useState<Template[]>([])
  const [pushes, setPushes] = useState<Push[]>([])
  const [pushTargets, setPushTargets] = useState<PushTarget[]>([])
  const [rollouts, setRollouts] = useState<Rollout[]>([])
  const [rolloutTargets, setRolloutTargets] = useState<RolloutTarget[]>([])
  const [lines, setLines] = useState<DiffLine[]>([])
  const [selected, setSelected] = useState('')
  const [summary, setSummary] = useState('')
  const [error, setError] = useState('')

  async function load() {
    const [status, list] = await Promise.all([
      studioApi<FleetView>('/api/v1/central/gateways'),
      studioApi<{ templates?: Template[]; licensed?: boolean }>('/api/v1/central/templates'),
    ])
    setFleet(status)
    setTemplates(list.templates ?? [])
    if (status.licensed && status.mode === 'central') {
      const [pushBody, rolloutBody] = await Promise.all([
        studioApi<{ pushes: Push[]; targets: PushTarget[] }>('/api/v1/central/pushes'),
        studioApi<{ rollouts: Rollout[]; targets: RolloutTarget[] }>('/api/v1/central/rollouts'),
      ])
      setPushes(pushBody.pushes ?? [])
      setPushTargets(pushBody.targets ?? [])
      setRollouts(rolloutBody.rollouts ?? [])
      setRolloutTargets(rolloutBody.targets ?? [])
      const spindle = (list.templates ?? []).filter((item) => item.key === 'spindle')
      if (spindle.length >= 2) {
        const from = spindle[0].version
        const to = spindle[spindle.length - 1].version
        const diff = await studioApi<{ lines: DiffLine[] }>(`/api/v1/central/templates/spindle/diff?from=${from}&to=${to}`)
        setLines(diff.lines ?? [])
      }
    }
    if (!selected && status.gateways?.[0]) setSelected(status.gateways[0].id)
  }

  useEffect(() => {
    void load().catch((err: unknown) => setError(describeError(err)))
  }, [])

  const groups = useMemo(() => {
    const names = new Map((fleet?.groups ?? []).map((group) => [group.id, group.name]))
    const buckets = new Map<string, Gateway[]>()
    for (const gateway of fleet?.gateways ?? []) {
      const key = gateway.site || names.get(gateway.groupId) || '未分组'
      const list = buckets.get(key) ?? []
      list.push(gateway)
      buckets.set(key, list)
    }
    return [...buckets.entries()]
  }, [fleet])

  async function openGateway(id: string) {
    setSelected(id)
    const body = await studioApi<{ note: string; summary: unknown }>(`/api/v1/central/gateways/${encodeURIComponent(id)}`)
    setSummary(`${body.note}\n${JSON.stringify(body.summary, null, 2)}`)
  }

  async function rollback(id: string) {
    await studioApi(`/api/v1/central/pushes/${encodeURIComponent(id)}/rollback`, { method: 'POST' })
    await load()
  }

  const edge = fleet?.mode === 'edge'

  return (
    <PageShell
      title='中心管理'
      description='边缘主动连出中心，适合在 NAT 后面。中心不可达时边缘继续采集。连接地址、口令、显示名、用户、许可证和 HTTPS 由现场管理；模板里列出的规则由中心管理。'
    >
      {error ? <p className='mb-3 text-sm text-destructive'>{error}</p> : null}
      {fleet && !fleet.licensed ? <p className='text-sm text-muted-foreground'>{fleet.message || '当前许可证不包含中心管理。'}</p> : null}
      {edge ? (
        <p className='mb-3 text-sm text-muted-foreground'>这台进程是边缘模式。用 --central 启动另一台 Host 才能看到机队、配置下发和升级推送。边缘页只说明本机是否已向中心注册。</p>
      ) : null}
      {fleet?.licensed && !edge ? (
        <div className='grid gap-4'>
          <div className='grid gap-4 lg:grid-cols-2' data-testid='fleet-overview'>
            {groups.map(([site, gateways]) => (
              <Card key={site}>
                <CardHeader><CardTitle className='text-base'>{site}</CardTitle></CardHeader>
                <CardContent className='grid gap-2'>
                  {gateways.map((gateway) => (
                    <button
                      key={gateway.id}
                      type='button'
                      className={`rounded-md border px-3 py-2 text-left text-sm ${selected === gateway.id ? 'border-primary' : ''}`}
                      onClick={() => void openGateway(gateway.id).catch((err: unknown) => setError(describeError(err)))}
                    >
                      <div className='flex items-center justify-between gap-2'>
                        <span className='font-medium'>{gateway.name}</span>
                        <Badge variant={gateway.status === 'online' ? 'default' : 'destructive'}>{statusText[gateway.status] ?? gateway.status}</Badge>
                      </div>
                      <div className='text-muted-foreground'>
                        {gateway.version} · {gateway.licenseSummary} · 设备 {gateway.deviceCount} · 在线链路 {gateway.onlineLinks} · 内存 {gateway.workingSetMb ?? '—'} MB
                      </div>
                    </button>
                  ))}
                </CardContent>
              </Card>
            ))}
          </div>

          {(fleet.alerts ?? []).length > 0 ? (
            <Card>
              <CardHeader><CardTitle className='text-base'>离线告警</CardTitle></CardHeader>
              <CardContent className='grid gap-1 text-sm'>
                {fleet.alerts.filter((item) => item.active).map((item) => (
                  <p key={`${item.gatewayId}-${item.raisedUnixMs}`}>{formatTime(item.raisedUnixMs)} {item.gatewayId} {item.message}</p>
                ))}
              </CardContent>
            </Card>
          ) : null}

          {summary ? (
            <Card>
              <CardHeader><CardTitle className='text-base'>网关摘要</CardTitle></CardHeader>
              <CardContent><pre className='whitespace-pre-wrap text-xs'>{summary}</pre></CardContent>
            </Card>
          ) : null}

          <Card>
            <CardHeader><CardTitle className='text-base'>配置模板差异</CardTitle></CardHeader>
            <CardContent data-testid='config-diff'>
              <p className='mb-2 text-sm text-muted-foreground'>
                {(templates ?? []).map((item) => `${item.name} v${item.version}`).join('，') || '还没有模板'}
              </p>
              <pre className='whitespace-pre-wrap font-mono text-xs'>
                {lines.map((line) => `${line.kind === 'add' ? '+' : line.kind === 'remove' ? '-' : ' '}${line.text}`).join('\n')}
              </pre>
              <Table className='mt-3'>
                <TableHeader>
                  <TableRow>
                    <TableHead>下发</TableHead>
                    <TableHead>策略</TableHead>
                    <TableHead>状态</TableHead>
                    <TableHead />
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {pushes.map((push) => (
                    <TableRow key={push.id}>
                      <TableCell>{push.templateKey} v{push.version}{push.rollback ? '（回滚）' : ''}</TableCell>
                      <TableCell>{push.conflictPolicy}</TableCell>
                      <TableCell>{statusText[push.status] ?? push.status}</TableCell>
                      <TableCell>
                        <Button size='sm' variant='outline' disabled={!admin} onClick={() => void rollback(push.id).catch((err: unknown) => setError(describeError(err)))}>回滚</Button>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
              {pushes[0]?.diffText ? <pre className='mt-3 whitespace-pre-wrap font-mono text-xs'>{pushes[0].diffText}</pre> : null}
              <p className='mt-2 text-xs text-muted-foreground'>
                目标：{pushTargets.map((item) => `${item.gatewayId} ${statusText[item.status] ?? item.status}`).join('，')}
              </p>
            </CardContent>
          </Card>

          <Card>
            <CardHeader><CardTitle className='text-base'>升级推送</CardTitle></CardHeader>
            <CardContent data-testid='rollout-progress' className='grid gap-3'>
              {rollouts.map((rollout) => (
                <div key={rollout.id}>
                  <p className='text-sm font-medium'>{rollout.version} · {statusText[rollout.status] ?? rollout.status}</p>
                  <div className='mt-2 grid gap-2'>
                    {rolloutTargets.filter((item) => item.rolloutId === rollout.id).map((target) => (
                      <div key={target.gatewayId} className='grid grid-cols-[8rem_6rem_1fr] items-center gap-2 text-sm'>
                        <span>{target.gatewayId}</span>
                        <Badge variant={target.status === 'failed' ? 'destructive' : target.status === 'staged' ? 'default' : 'secondary'}>
                          {statusText[target.status] ?? target.status}
                        </Badge>
                        <span className='text-muted-foreground'>{target.message}</span>
                      </div>
                    ))}
                  </div>
                </div>
              ))}
            </CardContent>
          </Card>
        </div>
      ) : null}
    </PageShell>
  )
}
