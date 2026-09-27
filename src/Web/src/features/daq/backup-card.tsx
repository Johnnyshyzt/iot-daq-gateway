import { useEffect, useRef, useState } from 'react'
import { toast } from 'sonner'
import { ConfirmDialog } from '@/components/confirm-dialog'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { describeError, studioApi } from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'

type OpsStatus = {
  database: string
  fileBackup: boolean
  schemaVersion: number
}

export function BackupCard() {
  const role = useAuthStore((state) => state.auth.user?.role[0])
  const admin = role === 'admin'
  const fileRef = useRef<HTMLInputElement>(null)
  const [status, setStatus] = useState<OpsStatus | null>(null)
  const [pending, setPending] = useState<File | null>(null)
  const [message, setMessage] = useState('')

  useEffect(() => {
    void studioApi<OpsStatus>('/api/v1/ops/status')
      .then(setStatus)
      .catch((error: unknown) => setMessage(describeError(error)))
  }, [])

  async function download() {
    const token = useAuthStore.getState().auth.accessToken
    const response = await fetch('/api/v1/ops/backup', {
      headers: token ? { Authorization: `Bearer ${token}` } : {},
    })
    if (!response.ok) {
      const body = (await response.json().catch(() => null)) as { message?: string } | null
      throw new Error(body?.message || '备份失败')
    }
    const blob = await response.blob()
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    const stamp = new Date().toISOString().slice(0, 10)
    link.href = url
    link.download = `iot-daq-gateway-${stamp}.db`
    link.click()
    URL.revokeObjectURL(url)
    toast.success('数据库备份已开始下载')
  }

  async function restore(file: File) {
    const token = useAuthStore.getState().auth.accessToken
    const response = await fetch('/api/v1/ops/restore', {
      method: 'POST',
      headers: {
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        'Content-Type': 'application/octet-stream',
      },
      body: file,
    })
    if (!response.ok) {
      const body = (await response.json().catch(() => null)) as { message?: string } | null
      throw new Error(body?.message || '恢复失败')
    }
    toast.success('数据库已恢复，正在刷新页面')
    window.location.assign('/')
  }

  return (
    <Card className='lg:col-span-2'>
      <CardHeader>
        <CardTitle>数据库备份</CardTitle>
      </CardHeader>
      <CardContent className='grid gap-3 text-sm'>
        <p className='text-muted-foreground'>
          {status?.fileBackup
            ? `当前是 SQLite（结构版本 ${status.schemaVersion}）。下载的是一份一致的数据库快照，不含登录口令。`
            : status
              ? `当前数据库是 ${status.database}。页面只备份 SQLite。PostgreSQL 请在数据库服务器上备份。`
              : '正在读取数据库状态…'}
        </p>
        <p className='text-muted-foreground'>
          登录口令在数据目录的 auth 文件夹里，页面恢复不会改掉正在使用的口令。恢复前会把当前库留一份 gateway.db.bak。
        </p>
        {!admin ? <p>只有管理员可以下载或恢复。</p> : null}
        <div className='flex flex-wrap gap-2'>
          <Button
            variant='outline'
            disabled={!admin || !status?.fileBackup}
            onClick={() => void download().catch((error: unknown) => setMessage(describeError(error)))}
          >
            下载备份
          </Button>
          <Button
            variant='destructive'
            disabled={!admin || !status?.fileBackup}
            onClick={() => fileRef.current?.click()}
          >
            从文件恢复
          </Button>
          <input
            ref={fileRef}
            type='file'
            accept='.db,application/octet-stream'
            className='hidden'
            onChange={(event) => {
              const file = event.target.files?.[0] ?? null
              event.target.value = ''
              setPending(file)
            }}
          />
        </div>
        {message ? <p className='text-destructive'>{message}</p> : null}
      </CardContent>
      <ConfirmDialog
        open={pending !== null}
        onOpenChange={(open) => {
          if (!open) setPending(null)
        }}
        title='恢复数据库'
        desc={`用「${pending?.name ?? ''}」替换当前数据库。草稿、已发布配置、采样和审计都会换成备份里的内容。登录口令不变。`}
        destructive
        confirmText='恢复'
        handleConfirm={() => {
          const file = pending
          setPending(null)
          if (file) void restore(file).catch((error: unknown) => setMessage(describeError(error)))
        }}
      />
    </Card>
  )
}
