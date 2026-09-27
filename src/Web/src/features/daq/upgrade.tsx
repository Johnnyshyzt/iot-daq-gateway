import { useEffect, useRef, useState } from 'react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { describeError, studioApi } from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'
import { PageShell } from './page-shell'

type UpgradeState = {
  phase: string
  version: string
  backupPath: string
  message: string
}

type UpgradeView = {
  state: UpgradeState | null
  limitations: string[]
  packagePresent: boolean
}

export function UpgradePage() {
  const admin = useAuthStore((state) => state.auth.user?.role[0]) === 'admin'
  const fileRef = useRef<HTMLInputElement>(null)
  const [view, setView] = useState<UpgradeView | null>(null)
  const [message, setMessage] = useState('')

  async function load() {
    setView(await studioApi<UpgradeView>('/api/v1/ops/upgrade'))
  }

  useEffect(() => {
    void load().catch((error: unknown) => setMessage(describeError(error)))
  }, [])

  async function upload(file: File) {
    const token = useAuthStore.getState().auth.accessToken
    const response = await fetch('/api/v1/ops/upgrade', {
      method: 'POST',
      headers: {
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        'Content-Type': 'application/octet-stream',
      },
      body: file,
    })
    const payload = (await response.json()) as { message?: string }
    if (!response.ok) throw new Error(payload.message || '升级包被拒绝')
    setMessage(payload.message || '已暂存')
    toast.success('升级包已暂存')
    await load()
  }

  async function apply() {
    const result = await studioApi<{ message: string }>('/api/v1/ops/upgrade/apply', { method: 'POST' })
    setMessage(result.message)
    toast.success('已请求在重启后应用')
    await load()
  }

  async function rollback() {
    const result = await studioApi<{ message: string }>('/api/v1/ops/upgrade/rollback', { method: 'POST' })
    setMessage(result.message)
    toast.success('数据库已回到升级前备份')
    await load()
  }

  return (
    <PageShell
      title='升级'
      description='上传发行包，校验签名和校验和，备份数据库，重启后才替换程序。采集在重启前不会停。'
    >
      <div className='grid gap-4'>
        <Card>
          <CardHeader>
            <CardTitle>暂存的包</CardTitle>
          </CardHeader>
          <CardContent className='space-y-3 text-sm'>
            <p>
              {view?.state
                ? `状态 ${view.state.phase}，版本 ${view.state.version || '未知'}。${view.state.message}`
                : '还没有暂存的升级包。'}
            </p>
            {message ? <p>{message}</p> : null}
            <div className='flex flex-wrap gap-2'>
              <Button disabled={!admin} onClick={() => fileRef.current?.click()}>
                上传 zip
              </Button>
              <Button variant='outline' disabled={!admin || !view?.packagePresent} onClick={() => void apply().catch((error: unknown) => setMessage(describeError(error)))}>
                请求重启后应用
              </Button>
              <Button variant='destructive' disabled={!admin || !view?.state?.backupPath} onClick={() => void rollback().catch((error: unknown) => setMessage(describeError(error)))}>
                恢复升级前数据库
              </Button>
              <input
                ref={fileRef}
                type='file'
                accept='.zip,application/zip'
                className='hidden'
                onChange={(event) => {
                  const file = event.target.files?.[0]
                  event.target.value = ''
                  if (file) void upload(file).catch((error: unknown) => setMessage(describeError(error)))
                }}
              />
            </div>
            {!admin ? <p className='text-muted-foreground'>只有管理员可以上传或应用。</p> : null}
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>做不到的事</CardTitle>
          </CardHeader>
          <CardContent>
            <ul className='list-disc space-y-2 pl-5 text-sm text-muted-foreground'>
              {(view?.limitations ?? []).map((item) => (
                <li key={item}>{item}</li>
              ))}
            </ul>
          </CardContent>
        </Card>
      </div>
    </PageShell>
  )
}
