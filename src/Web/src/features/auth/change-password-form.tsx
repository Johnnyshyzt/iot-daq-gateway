import { useState } from 'react'
import { useNavigate } from '@tanstack/react-router'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { PasswordInput } from '@/components/password-input'
import { describeError, studioApi } from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'

export function ChangePasswordForm({
  redirectHome = false,
}: {
  redirectHome?: boolean
}) {
  const navigate = useNavigate()
  const user = useAuthStore((state) => state.auth.user)
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [pending, setPending] = useState(false)

  async function submit() {
    if (newPassword.length < 8) {
      toast.error('新密码至少 8 位')
      return
    }
    if (newPassword !== confirmPassword) {
      toast.error('两次输入的新密码不一致')
      return
    }
    setPending(true)
    try {
      await studioApi('/api/v1/auth/password', {
        method: 'POST',
        body: JSON.stringify({ currentPassword, newPassword }),
      })
      if (user) {
        useAuthStore.getState().auth.setUser({ ...user, mustChangePassword: false })
      }
      toast.success('密码已修改')
      setCurrentPassword('')
      setNewPassword('')
      setConfirmPassword('')
      if (redirectHome) {
        await navigate({ to: '/' })
      }
    } catch (error) {
      toast.error(describeError(error))
    } finally {
      setPending(false)
    }
  }

  return (
    <form
      className='grid gap-3'
      onSubmit={(event) => {
        event.preventDefault()
        void submit()
      }}
    >
      <Field label='当前密码'>
        <PasswordInput
          autoComplete='current-password'
          value={currentPassword}
          onChange={(event) => setCurrentPassword(event.target.value)}
        />
      </Field>
      <Field label='新密码'>
        <PasswordInput
          autoComplete='new-password'
          value={newPassword}
          onChange={(event) => setNewPassword(event.target.value)}
        />
      </Field>
      <Field label='再次输入新密码'>
        <PasswordInput
          autoComplete='new-password'
          value={confirmPassword}
          onChange={(event) => setConfirmPassword(event.target.value)}
        />
      </Field>
      <p className='text-xs text-muted-foreground'>至少 8 位，不能与用户名相同，也不能改回 admin、engineer、viewer。</p>
      <Button type='submit' disabled={pending}>
        {pending ? '正在保存…' : '修改密码'}
      </Button>
    </form>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className='grid gap-1.5'>
      <Label>{label}</Label>
      {children}
    </div>
  )
}
