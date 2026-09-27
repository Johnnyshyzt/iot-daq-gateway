import { useEffect, useState } from 'react'
import { Link } from '@tanstack/react-router'
import { Check, Circle } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { describeError, studioApi } from '@/lib/studio-api'
import { PageShell } from './page-shell'

export type OnboardingStep = {
  id: string
  title: string
  detail: string
  done: boolean
  href: string
}

export type OnboardingStatus = {
  dismissed: boolean
  complete: boolean
  mustChangePassword: boolean
  steps: OnboardingStep[]
}

function stepPath(href: string): '/' | '/devices' | '/publish' | '/account/password' {
  if (href === '/devices' || href === '/publish' || href === '/account/password') return href
  return '/'
}

export function OnboardingBanner() {
  const [status, setStatus] = useState<OnboardingStatus | null>(null)

  useEffect(() => {
    void studioApi<OnboardingStatus>('/api/v1/onboarding')
      .then(setStatus)
      .catch(() => setStatus(null))
  }, [])

  if (!status || status.dismissed || status.complete) return null
  const done = status.steps.filter((step) => step.done).length
  const next = status.steps.find((step) => !step.done)

  return (
    <div className='mx-4 mb-1 rounded-lg border bg-card px-4 py-3'>
      <div className='flex flex-wrap items-center justify-between gap-3'>
        <div>
          <p className='text-sm font-medium'>上手引导 {done}/{status.steps.length}</p>
          <p className='text-sm text-muted-foreground'>
            {next ? `下一步：${next.title}` : '还差最后一项'}
          </p>
        </div>
        <div className='flex flex-wrap gap-2'>
          <Button asChild size='sm'>
            <Link to='/setup'>继续设置</Link>
          </Button>
          <Button
            size='sm'
            variant='outline'
            onClick={() =>
              void studioApi('/api/v1/onboarding/dismiss', { method: 'POST' })
                .then(() => setStatus({ ...status, dismissed: true }))
                .catch((error: unknown) => toast.error(describeError(error)))
            }
          >
            稍后提醒
          </Button>
        </div>
      </div>
    </div>
  )
}

export function SetupPage() {
  const [status, setStatus] = useState<OnboardingStatus | null>(null)
  const [error, setError] = useState('')

  function load() {
    return studioApi<OnboardingStatus>('/api/v1/onboarding')
      .then((next) => {
        setStatus(next)
        setError('')
      })
      .catch((err: unknown) => setError(describeError(err)))
  }

  useEffect(() => {
    void load()
  }, [])

  const done = status?.steps.filter((step) => step.done).length ?? 0

  return (
    <PageShell
      title='上手引导'
      description='新装好的网关按这五步就能看到第一台设备的实时状态。种子里的模拟器会让其中几步一开始就是完成的。'
    >
      {!status && !error ? <p className='text-sm text-muted-foreground'>正在读取引导进度…</p> : null}
      {error ? <p className='text-sm text-destructive'>{error}</p> : null}
      {status ? (
        <div className='grid max-w-3xl gap-4'>
          <p className='text-sm text-muted-foreground'>
            已完成 {done}/{status.steps.length}
            {status.complete ? '。可以在总览看车间状态，或关掉这条引导。' : '。'}
          </p>
          <ol className='grid gap-3'>
            {status.steps.map((step, index) => (
              <li key={step.id}>
                <Card>
                  <CardHeader className='flex flex-row items-start justify-between gap-3'>
                    <CardTitle className='flex items-center gap-2 text-base'>
                      {step.done ? (
                        <Check className='size-4 text-emerald-600 dark:text-emerald-400' />
                      ) : (
                        <Circle className='size-4 text-muted-foreground' />
                      )}
                      {index + 1}. {step.title}
                    </CardTitle>
                    <Button asChild size='sm' variant={step.done ? 'outline' : 'default'}>
                      <Link to={stepPath(step.href)}>{step.done ? '查看' : '去完成'}</Link>
                    </Button>
                  </CardHeader>
                  <CardContent className='grid gap-3 text-sm text-muted-foreground'>
                    <p>{step.detail}</p>
                    {step.id === 'password' && !step.done && !status.mustChangePassword ? (
                      <div>
                        <Button
                          size='sm'
                          variant='outline'
                          onClick={() =>
                            void studioApi('/api/v1/onboarding/password-ack', { method: 'POST' })
                              .then(() => load())
                              .catch((err: unknown) => setError(describeError(err)))
                          }
                        >
                          演示环境，稍后修改
                        </Button>
                      </div>
                    ) : null}
                  </CardContent>
                </Card>
              </li>
            ))}
          </ol>
          {!status.dismissed ? (
            <Button
              variant='outline'
              className='w-fit'
              onClick={() =>
                void studioApi('/api/v1/onboarding/dismiss', { method: 'POST' })
                  .then(() => load())
                  .then(() => toast.success('总览上的引导已关闭，这个页面仍可打开'))
                  .catch((err: unknown) => setError(describeError(err)))
              }
            >
              关闭总览上的提醒
            </Button>
          ) : (
            <p className='text-sm text-muted-foreground'>总览上的提醒已关闭。需要时仍可从侧栏打开这一页。</p>
          )}
        </div>
      ) : null}
    </PageShell>
  )
}
