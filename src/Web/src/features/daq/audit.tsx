import { useEffect, useState } from 'react'
import { describeError, roleLabel, studioApi } from '@/lib/studio-api'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { PageShell } from './page-shell'

type AuditEvent = {
  id: number
  unixMs: number
  username: string
  role: string
  action: string
  target: string
  detail: string
}

const actionLabels: Record<string, string> = {
  'config.publish': '发布',
  'config.rollback': '回滚',
  'config.import': '导入',
  'device.upsert': '保存设备',
  'device.delete': '删除设备',
  'device.collection': '采集启停',
  'template.upsert': '保存模板',
  'template.delete': '删除模板',
  'points.upsert': '点位覆盖',
  'mqtt.update': '北向 MQTT',
  'gateway.update': '网关草稿',
  'settings.update': '系统设置',
  'group.upsert': '保存分组',
  'group.delete': '删除分组',
  'password.change': '修改密码',
  'viz.settings': '班次与保留',
  'ops.backup': '下载备份',
  'notify.channel': '保存通知通道',
  'notify.channel.delete': '删除通知通道',
  'notify.rule': '保存通知规则',
  'notify.rule.delete': '删除通知规则',
  'notify.test': '测试通知',
  'notify.retry': '重试通知',
  'notify.report': '报表计划',
  'reliability.update': '可靠性设置',
  'ops.restore': '恢复数据库',
  'httppush.save': '保存 HTTP 推送',
  'httppush.delete': '删除 HTTP 推送',
  'httppush.test': '测试 HTTP 推送',
  'apikey.create': '创建查询密钥',
  'apikey.revoke': '吊销查询密钥',
  'opcua.update': 'OPC UA 设置',
  'onboarding.dismiss': '关闭引导',
  'auth.login': '登录',
  'auth.login.fail': '登录失败',
  'auth.lockout': '登录锁定',
  'auth.denied': '拒绝访问',
  'user.create': '创建用户',
  'user.update': '更新用户',
  'user.delete': '删除用户',
  'https.self-signed': '生成 HTTPS 证书',
  'https.import': '导入 HTTPS 证书',
  'https.disable': '关闭 HTTPS',
  'device.self-test': '连通性自检',
  'ops.diagnose': '导出诊断包',
  'security.clock_rollback': '时钟回拨',
  'security.state_tamper': '授权状态被改',
  'security.ack': '确认授权状态',
}

export function AuditPage() {
  const [events, setEvents] = useState<AuditEvent[] | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    void studioApi<{ events: AuditEvent[] }>('/api/v1/audit?limit=100')
      .then((body) => setEvents(body.events))
      .catch((err: unknown) => setError(describeError(err)))
  }, [])

  return (
    <PageShell
      title='审计'
      description='配置变更留在数据库里，最多保留最近 500 条。这里不记录密码本身。'
    >
      {error ? <p className='text-sm text-destructive'>{error}</p> : null}
      {!events && !error ? <p className='text-sm text-muted-foreground'>正在读取审计记录…</p> : null}
      {events && events.length === 0 ? (
        <p className='text-sm text-muted-foreground'>还没有配置变更。保存设备、发布或修改密码之后会出现在这里。</p>
      ) : null}
      {events && events.length > 0 ? (
        <div className='overflow-x-auto rounded-md border'>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>时间</TableHead>
                <TableHead>用户</TableHead>
                <TableHead>动作</TableHead>
                <TableHead>对象</TableHead>
                <TableHead>说明</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {events.map((event) => (
                <TableRow key={event.id}>
                  <TableCell className='whitespace-nowrap text-xs'>
                    {new Date(event.unixMs).toLocaleString('zh-CN', { hour12: false })}
                  </TableCell>
                  <TableCell>
                    {event.username || '—'}
                    <span className='text-muted-foreground'> · {roleLabel(event.role)}</span>
                  </TableCell>
                  <TableCell>{actionLabels[event.action] ?? event.action}</TableCell>
                  <TableCell className='font-mono text-xs'>{event.target}</TableCell>
                  <TableCell className='max-w-xs truncate text-sm text-muted-foreground'>{event.detail}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      ) : null}
    </PageShell>
  )
}
