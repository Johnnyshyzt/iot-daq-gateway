import { createFileRoute, redirect } from '@tanstack/react-router'
import { UsersPage } from '@/features/daq/users'
import { useAuthStore } from '@/stores/auth-store'

export const Route = createFileRoute('/_authenticated/users')({
  beforeLoad: () => {
    if (useAuthStore.getState().auth.user?.role[0] !== 'admin') {
      throw redirect({ to: '/' })
    }
  },
  component: UsersPage,
})
