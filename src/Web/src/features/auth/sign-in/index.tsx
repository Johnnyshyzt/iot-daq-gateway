import { useEffect, useState } from 'react'
import { useSearch } from '@tanstack/react-router'
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { AuthLayout } from '../auth-layout'
import { UserAuthForm } from './components/user-auth-form'

export function SignIn() {
  const { redirect } = useSearch({ from: '/(auth)/sign-in' })
  const [accountMode, setAccountMode] = useState('')
  const [posture, setPosture] = useState('正在读取本机登录方式…')

  useEffect(() => {
    void fetch('/api/v1/auth/posture')
      .then(async (response) => {
        if (!response.ok) throw new Error('posture')
        return (await response.json()) as { mode?: string; message?: string }
      })
      .then((body) => {
        setAccountMode(body.mode ?? '')
        setPosture(body.message || '请使用本机账号登录。')
      })
      .catch(() => {
        setPosture(
          '无法读取登录方式。本机演示可试 admin / admin；现场包请查看 data\\auth\\bootstrap-password.txt，不要把默认口令留在客户机器上。'
        )
      })
  }, [])

  return (
    <AuthLayout>
      <Card className='max-w-sm gap-4'>
        <CardHeader>
          <CardTitle className='text-lg tracking-tight'>登录</CardTitle>
          <CardDescription>
            {accountMode === 'field'
              ? '现场采集机。一次性引导密码登录后必须修改。'
              : '进入采集配置台，编辑 Fanuc 设备、点位和 MQTT。'}
          </CardDescription>
        </CardHeader>
        <CardContent>
          <UserAuthForm redirectTo={redirect} accountMode={accountMode} />
        </CardContent>
        <CardFooter>
          <p className='text-sm text-muted-foreground'>{posture}</p>
        </CardFooter>
      </Card>
    </AuthLayout>
  )
}
