import { useEffect, useState } from 'react'
import { toast } from 'sonner'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { actionLabel, canWrite, describeError, studioApi, StudioApiError, type ValidationIssue } from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'
import { PageShell } from './page-shell'

type Diff = {
  dirty: boolean
  changes: Array<{ path: string; kind: string; summary: string }>
}
type Revision = {
  revision: string
  createdAt: string
  action: string
  note?: string | null
}

export function PublishPage() {
  const writable = canWrite(useAuthStore((state) => state.auth.user?.role[0]))
  const [diff, setDiff] = useState<Diff | null>(null)
  const [revisions, setRevisions] = useState<Revision[]>([])
  const [note, setNote] = useState('')
  const [issues, setIssues] = useState<ValidationIssue[]>([])
  const [message, setMessage] = useState('')

  function fail(error: unknown) {
    if (error instanceof StudioApiError) {
      setIssues(error.issues)
      setMessage(error.message)
      return
    }
    setMessage(describeError(error))
  }

  async function reload() {
    const [nextDiff, nextRevisions] = await Promise.all([
      studioApi<Diff>('/api/v1/config/diff'),
      studioApi<{ revisions: Revision[] }>('/api/v1/config/revisions'),
    ])
    setDiff(nextDiff)
    setRevisions(nextRevisions.revisions)
  }

  useEffect(() => {
    void reload().catch((error: unknown) => setMessage(describeError(error)))
  }, [])

  async function validate() {
    const result = await studioApi<{ valid: boolean; issues: Array<{ message: string }> }>(
      '/api/v1/config/validate',
      { method: 'POST' }
    )
    setIssues(result.issues)
    setMessage(result.valid ? '校验通过，可以发布' : '校验未通过，请先处理下面的错误')
  }

  async function publish() {
    const result = await studioApi<{
      revision: string
      unchanged: boolean
      issues: Array<{ message: string }>
    }>('/api/v1/config/publish', {
      method: 'POST',
      body: JSON.stringify({ note }),
    })
    setIssues(result.issues)
    setMessage(result.unchanged ? '内容没有变化' : `已发布 ${result.revision.slice(0, 12)}。运行态会切到这份修订。`)
    toast.success(result.unchanged ? '修订未变化' : '已发布')
    await reload()
  }

  async function rollback(revision: string) {
    await studioApi('/api/v1/config/rollback', {
      method: 'POST',
      body: JSON.stringify({ revision }),
    })
    toast.success('已回滚')
    await reload()
  }

  return (
    <PageShell
      title='发布'
      description='校验草稿后发布到 data/published。同一 Host 进程会立刻重新加载采集。'
    >
      <div className='grid gap-4 lg:grid-cols-2'>
        <Card>
          <CardHeader className='flex flex-row items-center justify-between'>
            <CardTitle>草稿差异</CardTitle>
            <Badge variant={diff?.dirty ? 'default' : 'secondary'}>
              {diff?.dirty ? '有未发布修改' : '已同步'}
            </Badge>
          </CardHeader>
          <CardContent className='space-y-3'>
            {(diff?.changes.length ?? 0) === 0 ? (
              <p className='text-sm text-muted-foreground'>没有差异。</p>
            ) : (
              <ul className='space-y-1 text-sm'>
                {diff?.changes.map((change) => (
                  <li key={`${change.path}-${change.summary}`}>
                    <span className='font-medium'>{change.path}</span> {change.summary}
                  </li>
                ))}
              </ul>
            )}
            <Input
              placeholder='发布说明'
              value={note}
              disabled={!writable}
              onChange={(event) => setNote(event.target.value)}
            />
            <div className='flex gap-2'>
              <Button variant='outline' disabled={!writable} onClick={() => void validate().catch(fail)}>
                校验
              </Button>
              <Button disabled={!writable} onClick={() => void publish().catch(fail)}>
                发布
              </Button>
            </div>
            {message ? <p className='text-sm'>{message}</p> : null}
            {issues.map((issue) => (
              <p
                key={`${issue.path}-${issue.message}`}
                className={issue.severity === 'warning' ? 'text-sm text-amber-700 dark:text-amber-400' : 'text-sm text-destructive'}
              >
                {issue.severity === 'warning' ? '警告' : '错误'}
                {issue.path ? ` · ${issue.path}` : ''}：{issue.message}
              </p>
            ))}
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>修订历史</CardTitle>
          </CardHeader>
          <CardContent>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>修订</TableHead>
                  <TableHead>动作</TableHead>
                  <TableHead />
                </TableRow>
              </TableHeader>
              <TableBody>
                {revisions.map((item) => (
                  <TableRow key={item.revision}>
                    <TableCell>
                      <div className='font-mono text-xs'>{item.revision.slice(0, 12)}</div>
                      <div className='text-xs text-muted-foreground'>{item.note || actionLabel(item.action)}</div>
                    </TableCell>
                    <TableCell>{actionLabel(item.action)}</TableCell>
                    <TableCell className='text-end'>
                      <Button
                        size='sm'
                        variant='outline'
                        disabled={!writable}
                        onClick={() => void rollback(item.revision).catch(fail)}
                      >
                        回滚
                      </Button>
                    </TableCell>
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
