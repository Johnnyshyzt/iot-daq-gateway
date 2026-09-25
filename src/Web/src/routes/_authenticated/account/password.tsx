import { createFileRoute } from '@tanstack/react-router'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { ChangePasswordForm } from '@/features/auth/change-password-form'
import { PageShell } from '@/features/daq/page-shell'
import { useAuthStore } from '@/stores/auth-store'

export const Route = createFileRoute('/_authenticated/account/password')({
  component: ChangePasswordPage,
})

function ChangePasswordPage() {
  const mustChange = useAuthStore((state) => state.auth.user?.mustChangePassword)

  return (
    <PageShell
      title='修改密码'
      description={
        mustChange
          ? '首次登录必须修改密码。改完之前不能编辑设备、点位或 MQTT。'
          : '修改当前本地账号的密码。角色仍是 admin、engineer 或 viewer。'
      }
    >
      <Card className='max-w-md'>
        <CardHeader>
          <CardTitle>本地账号</CardTitle>
        </CardHeader>
        <CardContent>
          {mustChange ? (
            <p className='mb-3 text-sm text-muted-foreground'>
              现场包不把 admin/admin 当作长期口令。请用一次性引导密码作为当前密码，然后设置新密码。
            </p>
          ) : null}
          <ChangePasswordForm redirectHome />
        </CardContent>
      </Card>
    </PageShell>
  )
}
