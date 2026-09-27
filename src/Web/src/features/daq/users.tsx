import { useEffect, useState } from 'react'
import { toast } from 'sonner'
import { ConfirmDialog } from '@/components/confirm-dialog'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { describeError, roleLabel, studioApi } from '@/lib/studio-api'
import { PageShell } from './page-shell'

type Account = {
  username: string
  role: string
  mustChangePassword: boolean
}

const roles = [
  { id: 'admin', label: '管理员' },
  { id: 'engineer', label: '工程师' },
  { id: 'operator', label: '操作员（只读）' },
  { id: 'viewer', label: '只读' },
]

export function UsersPage() {
  const [users, setUsers] = useState<Account[]>([])
  const [username, setUsername] = useState('')
  const [role, setRole] = useState('engineer')
  const [password, setPassword] = useState('')
  const [message, setMessage] = useState('')
  const [pendingDelete, setPendingDelete] = useState('')

  async function reload() {
    const body = await studioApi<{ users: Account[] }>('/api/v1/users')
    setUsers(body.users)
  }

  useEffect(() => {
    void reload().catch((error: unknown) => setMessage(describeError(error)))
  }, [])

  async function create() {
    setMessage('')
    await studioApi('/api/v1/users', {
      method: 'POST',
      body: JSON.stringify({ username, role, password }),
    })
    setUsername('')
    setPassword('')
    await reload()
    toast.success('用户已创建')
  }

  async function update(account: Account, nextRole: string, nextPassword: string) {
    setMessage('')
    await studioApi(`/api/v1/users/${encodeURIComponent(account.username)}`, {
      method: 'PUT',
      body: JSON.stringify({ role: nextRole, password: nextPassword || undefined }),
    })
    await reload()
    toast.success('已保存')
  }

  async function remove(name: string) {
    await studioApi(`/api/v1/users/${encodeURIComponent(name)}`, { method: 'DELETE' })
    setPendingDelete('')
    await reload()
    toast.success('已删除')
  }

  return (
    <PageShell title='用户' description='管理员维护账号。工程师可改配置。操作员和只读账号不能改配置。'>
      {message ? <p className='mb-3 text-sm text-destructive'>{message}</p> : null}
      <div className='grid gap-4 lg:grid-cols-2' data-testid='user-management'>
        <Card>
          <CardHeader>
            <CardTitle>新建用户</CardTitle>
          </CardHeader>
          <CardContent className='space-y-3'>
            <div>
              <Label>用户名</Label>
              <Input value={username} onChange={(event) => setUsername(event.target.value)} />
            </div>
            <div>
              <Label>角色</Label>
              <Select value={role} onValueChange={setRole}>
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {roles.map((item) => (
                    <SelectItem key={item.id} value={item.id}>
                      {item.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div>
              <Label>密码</Label>
              <Input type='password' value={password} onChange={(event) => setPassword(event.target.value)} />
            </div>
            <p className='text-sm text-muted-foreground'>至少 8 位，同时包含字母和数字。不能与用户名相同，也不能使用演示口令。</p>
            <Button
              onClick={() => void create().catch((error: unknown) => setMessage(describeError(error)))}
            >
              创建
            </Button>
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>现有用户</CardTitle>
          </CardHeader>
          <CardContent>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>用户</TableHead>
                  <TableHead>角色</TableHead>
                  <TableHead />
                </TableRow>
              </TableHeader>
              <TableBody>
                {users.map((account) => (
                  <UserRow
                    key={account.username}
                    account={account}
                    onSave={(nextRole, nextPassword) => update(account, nextRole, nextPassword)}
                    onDelete={() => setPendingDelete(account.username)}
                    onError={(error) => setMessage(describeError(error))}
                  />
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      </div>
      <ConfirmDialog
        open={pendingDelete.length > 0}
        onOpenChange={(open) => {
          if (!open) setPendingDelete('')
        }}
        title='删除用户'
        desc={`删除 ${pendingDelete} 后不能再登录。最后一个管理员不能删除。`}
        destructive
        confirmText='删除'
        handleConfirm={() => void remove(pendingDelete).catch((error: unknown) => setMessage(describeError(error)))}
      />
    </PageShell>
  )
}

function UserRow({
  account,
  onSave,
  onDelete,
  onError,
}: {
  account: Account
  onSave: (role: string, password: string) => Promise<void>
  onDelete: () => void
  onError: (error: unknown) => void
}) {
  const [role, setRole] = useState(account.role)
  const [password, setPassword] = useState('')
  return (
    <TableRow>
      <TableCell>
        {account.username}
        {account.mustChangePassword ? <div className='text-xs text-muted-foreground'>待修改密码</div> : null}
      </TableCell>
      <TableCell>
        <Select value={role} onValueChange={setRole}>
          <SelectTrigger className='w-36'>
            <SelectValue placeholder={roleLabel(account.role)} />
          </SelectTrigger>
          <SelectContent>
            {roles.map((item) => (
              <SelectItem key={item.id} value={item.id}>
                {item.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Input
          className='mt-2'
          type='password'
          placeholder='留空则不改密码'
          value={password}
          onChange={(event) => setPassword(event.target.value)}
        />
      </TableCell>
      <TableCell className='space-x-2 whitespace-nowrap'>
        <Button
          size='sm'
          variant='outline'
          onClick={() => void onSave(role, password).then(() => setPassword('')).catch(onError)}
        >
          保存
        </Button>
        <Button size='sm' variant='outline' onClick={onDelete}>
          删除
        </Button>
      </TableCell>
    </TableRow>
  )
}
